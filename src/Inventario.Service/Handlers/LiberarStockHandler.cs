using Inventario.Service.Abstracciones;
using TiendaNorte.Contracts;

namespace Inventario.Service.Handlers;

/// <summary>
/// Compensación LiberarStock: devuelve al stock lo reservado.
/// Siempre termina bien: publica StockLiberado aunque no haya nada que liberar,
/// para que la saga pueda terminar.
/// </summary>
public sealed class LiberarStockHandler(
    IInventarioRepository repositorio,
    IRespuestaPublisher publisher,
    TimeProvider tiempo,
    ILogger<LiberarStockHandler> logger)
{
    public async Task ManejarAsync(Mensaje<LiberarStock> comando, CancellationToken ct = default)
    {
        var reservaId = comando.Datos.ReservaId;

        await repositorio.IniciarTransaccionAsync(ct);
        try
        {
            // Idempotencia
            if (await repositorio.MensajeYaProcesadoAsync(comando.MessageId, ct))
            {
                await repositorio.DeshacerTransaccionAsync(ct);
                logger.LogInformation("LiberarStock {MessageId} ya procesado: se ignora.", comando.MessageId);
                return;
            }

            // Paso 1: leer la reserva bloqueándola (SELECT estado ... FOR UPDATE)
            var estado = await repositorio.ObtenerEstadoReservaBloqueandoAsync(reservaId, ct);

            if (estado == "Activa")
            {
                // Paso 2: devolver el stock de cada ítem y marcar la reserva como Liberada
                await repositorio.LiberarReservaAsync(reservaId, ct);
                logger.LogInformation("Reserva {ReservaId} liberada", reservaId);
            }
            else
            {
                // Paso 3: ya estaba Liberada o no existe: no se toca el stock
                logger.LogWarning("La reserva {ReservaId} no está activa (estado: {Estado}). No se modifica el stock.",
                    reservaId, estado ?? "no existe");
            }

            // Paso 4: registrar el messageId y confirmar
            await repositorio.RegistrarMensajeProcesadoAsync(comando.MessageId, ct);
            await repositorio.ConfirmarTransaccionAsync(ct);
        }
        catch
        {
            await repositorio.DeshacerTransaccionAsync(CancellationToken.None);
            throw;
        }

        // Paso 5: publicar StockLiberado en los tres casos
        var ahora = tiempo.GetUtcNow().UtcDateTime;
        await publisher.PublicarAsync(Mensaje.ResponderA(comando, new StockLiberado(reservaId), ahora), ct);
    }
}
