using Pagos.Service.Abstracciones;
using Pagos.Service.Reglas;
using TiendaNorte.Contracts;

namespace Pagos.Service.Handlers;

/// <summary>
/// Lógica del comando Cobrar. Depende solo de interfaces (repositorio y publicador),
/// así las pruebas del grupo C la ejecutan con dobles, sin PostgreSQL ni RabbitMQ.
/// </summary>
public sealed class CobrarHandler(
    IPagosRepository repositorio,
    IRespuestaPublisher publisher,
    ValidadorDePago validador,
    TimeProvider tiempo,
    ILogger<CobrarHandler> logger)
{
    private const string CaracteresCodigo = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task ManejarAsync(Mensaje<Cobrar> comando, CancellationToken ct = default)
    {
        var monto = comando.Datos.Monto;
        var tarjeta = comando.Datos.Tarjeta;
        var ahora = tiempo.GetUtcNow().UtcDateTime;

        RechazoDePago? rechazo;
        string? codigoAutorizacion = null;
        string pagoId;

        await repositorio.IniciarTransaccionAsync(ct);
        try
        {
            // Paso 3: idempotencia
            if (await repositorio.MensajeYaProcesadoAsync(comando.MessageId, ct))
            {
                await repositorio.DeshacerTransaccionAsync(ct);
                logger.LogInformation("Cobrar {MessageId} ya procesado: se ignora.", comando.MessageId);
                return;
            }

            // Paso 4: reglas de rechazo en orden
            rechazo = validador.Validar(monto, tarjeta);

            // Paso 5: si no hay motivo, código de autorización de 6 caracteres al azar
            if (rechazo is null)
                codigoAutorizacion = new string(Random.Shared.GetItems(CaracteresCodigo.AsSpan(), 6));

            // Paso 6: registrar el intento (aprobado o rechazado) con solo los últimos 4 dígitos
            var pago = new NuevoPago(
                comando.PedidoId,
                comando.SagaId,
                monto,
                rechazo is null ? "Aprobado" : "Rechazado",
                rechazo?.Motivo,
                UltimosCuatro(tarjeta.Numero),
                tarjeta.Titular ?? string.Empty,
                codigoAutorizacion,
                ahora);

            pagoId = await repositorio.RegistrarPagoAsync(pago, ct);
            await repositorio.RegistrarMensajeProcesadoAsync(comando.MessageId, ct);
            await repositorio.ConfirmarTransaccionAsync(ct);
        }
        catch
        {
            await repositorio.DeshacerTransaccionAsync(CancellationToken.None);
            throw;
        }

        // Paso 7: publicar el resultado DESPUÉS del commit
        if (rechazo is null)
        {
            logger.LogInformation("Pago {PagoId} aprobado ({Codigo}) para el pedido {PedidoId}", pagoId, codigoAutorizacion, comando.PedidoId);
            await publisher.PublicarAsync(
                Mensaje.ResponderA(comando, new PagoAprobado(pagoId, monto, codigoAutorizacion!), ahora), ct);
        }
        else
        {
            logger.LogInformation("Pago {PagoId} rechazado ({Motivo}) para el pedido {PedidoId}", pagoId, rechazo.Motivo, comando.PedidoId);
            await publisher.PublicarAsync(
                Mensaje.ResponderA(comando, new PagoRechazado(pagoId, rechazo.Motivo, rechazo.Detalle, rechazo.Limite), ahora), ct);
        }
    }

    /// <summary>Nunca se guarda el número completo de la tarjeta.</summary>
    private static string UltimosCuatro(string? numero) =>
        numero is { Length: >= 4 } ? numero[^4..] : (numero ?? string.Empty).PadLeft(4, '*');
}
