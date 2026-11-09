using Logistica.Service.Abstracciones;
using TiendaNorte.Contracts;

namespace Logistica.Service.Handlers;

/// <summary>
/// Compensación AnularEnvio. Siempre termina bien: publica EnvioAnulado aunque no haya
/// nada que anular. En la saga obligatoria no se dispara (el envío es el último paso),
/// pero el enunciado la exige igual.
/// </summary>
public sealed class AnularEnvioHandler(
    IEnviosRepository repositorio,
    IRespuestaPublisher publisher,
    TimeProvider tiempo,
    ILogger<AnularEnvioHandler> logger)
{
    public async Task ManejarAsync(Mensaje<AnularEnvio> comando, CancellationToken ct = default)
    {
        var envioId = comando.Datos.EnvioId;

        await repositorio.IniciarTransaccionAsync(ct);
        try
        {
            // Idempotencia
            if (await repositorio.MensajeYaProcesadoAsync(comando.MessageId, ct))
            {
                await repositorio.DeshacerTransaccionAsync(ct);
                logger.LogInformation("AnularEnvio {MessageId} ya procesado: se ignora.", comando.MessageId);
                return;
            }

            // Paso 1: leer el envío bloqueándolo (SELECT estado ... FOR UPDATE)
            var estado = await repositorio.ObtenerEstadoEnvioBloqueandoAsync(envioId, ct);

            if (estado == "Programado")
            {
                // Paso 2: Anulado. Libera el cupo, porque el conteo solo incluye envíos Programado
                await repositorio.AnularEnvioAsync(envioId, ct);
                logger.LogInformation("Envío {EnvioId} anulado", envioId);
            }
            else
            {
                // Paso 3: ya Anulado o inexistente: no cambia nada
                logger.LogWarning("El envío {EnvioId} no está programado (estado: {Estado}).", envioId, estado ?? "no existe");
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

        var ahora = tiempo.GetUtcNow().UtcDateTime;
        await publisher.PublicarAsync(Mensaje.ResponderA(comando, new EnvioAnulado(envioId), ahora), ct);
    }
}
