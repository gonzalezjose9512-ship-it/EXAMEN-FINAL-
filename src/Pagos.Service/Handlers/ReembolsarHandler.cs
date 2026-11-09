using Pagos.Service.Abstracciones;
using TiendaNorte.Contracts;

namespace Pagos.Service.Handlers;

/// <summary>
/// Compensación Reembolsar. Siempre termina bien: publica PagoReembolsado aunque no haya
/// nada que reembolsar (con monto 0), para que la saga pueda terminar.
/// </summary>
public sealed class ReembolsarHandler(
    IPagosRepository repositorio,
    IRespuestaPublisher publisher,
    TimeProvider tiempo,
    ILogger<ReembolsarHandler> logger)
{
    public async Task ManejarAsync(Mensaje<Reembolsar> comando, CancellationToken ct = default)
    {
        var pagoId = comando.Datos.PagoId;
        var ahora = tiempo.GetUtcNow().UtcDateTime;
        decimal monto;

        await repositorio.IniciarTransaccionAsync(ct);
        try
        {
            // Idempotencia
            if (await repositorio.MensajeYaProcesadoAsync(comando.MessageId, ct))
            {
                await repositorio.DeshacerTransaccionAsync(ct);
                logger.LogInformation("Reembolsar {MessageId} ya procesado: se ignora.", comando.MessageId);
                return;
            }

            // Paso 1: leer el pago bloqueándolo (SELECT estado, monto ... FOR UPDATE)
            var pago = await repositorio.ObtenerPagoBloqueandoAsync(pagoId, ct);

            switch (pago?.Estado)
            {
                case "Aprobado":
                    // Paso 2: marcarlo Reembolsado; el evento lleva el monto del pago
                    await repositorio.MarcarReembolsadoAsync(pagoId, ahora, ct);
                    monto = pago.Monto;
                    logger.LogInformation("Pago {PagoId} reembolsado ({Monto})", pagoId, monto);
                    break;

                case "Reembolsado":
                    // Paso 3: ya estaba reembolsado: no cambia nada, mismo monto
                    monto = pago.Monto;
                    break;

                default:
                    // Paso 4: Rechazado o inexistente: no cambia nada, monto 0
                    monto = 0;
                    logger.LogWarning("El pago {PagoId} no se puede reembolsar (estado: {Estado}).",
                        pagoId, pago?.Estado ?? "no existe");
                    break;
            }

            // Paso 5: registrar el messageId y confirmar
            await repositorio.RegistrarMensajeProcesadoAsync(comando.MessageId, ct);
            await repositorio.ConfirmarTransaccionAsync(ct);
        }
        catch
        {
            await repositorio.DeshacerTransaccionAsync(CancellationToken.None);
            throw;
        }

        await publisher.PublicarAsync(Mensaje.ResponderA(comando, new PagoReembolsado(pagoId, monto), ahora), ct);
    }
}
