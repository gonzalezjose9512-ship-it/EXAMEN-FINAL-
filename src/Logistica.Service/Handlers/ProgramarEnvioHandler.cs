using Logistica.Service.Abstracciones;
using Logistica.Service.Reglas;
using Microsoft.Extensions.Options;
using TiendaNorte.Contracts;

namespace Logistica.Service.Handlers;

/// <summary>
/// Lógica del comando ProgramarEnvio. Depende solo de interfaces (repositorio y publicador),
/// así las pruebas del grupo C la ejecutan con dobles, sin PostgreSQL ni RabbitMQ.
/// Valida en orden: dirección → fecha → cupo.
/// </summary>
public sealed class ProgramarEnvioHandler(
    IEnviosRepository repositorio,
    IRespuestaPublisher publisher,
    CalendarioHabil calendario,
    IOptions<OpcionesLogistica> opciones,
    TimeProvider tiempo,
    ILogger<ProgramarEnvioHandler> logger)
{
    public async Task ManejarAsync(Mensaje<ProgramarEnvio> comando, CancellationToken ct = default)
    {
        var direccion = comando.Datos.Direccion;
        var cupoDiario = opciones.Value.CupoDiario;
        var ahora = tiempo.GetUtcNow().UtcDateTime;

        EnvioRechazado? rechazo = null;
        EnvioProgramado? programado = null;

        await repositorio.IniciarTransaccionAsync(ct);
        try
        {
            // Paso 3: idempotencia
            if (await repositorio.MensajeYaProcesadoAsync(comando.MessageId, ct))
            {
                await repositorio.DeshacerTransaccionAsync(ct);
                logger.LogInformation("ProgramarEnvio {MessageId} ya procesado: se ignora.", comando.MessageId);
                return;
            }

            // Paso 4: dirección completa
            var campoFaltante = BuscarCampoFaltante(direccion);
            if (campoFaltante is not null)
            {
                rechazo = new EnvioRechazado(MotivosRechazo.DireccionInvalida, Detalle: $"Falta {campoFaltante}");
            }
            // Paso 5: la fecha preferida no puede caer en fin de semana
            else if (comando.Datos.FechaPreferida is { } preferida && CalendarioHabil.EsFinDeSemana(preferida))
            {
                rechazo = new EnvioRechazado(MotivosRechazo.FechaInvalida, Fecha: preferida);
            }
            else
            {
                var fechaDespacho = calendario.CalcularFechaDespacho(comando.Datos.FechaPreferida);
                var fechaEntrega = CalendarioHabil.CalcularEntregaEstimada(fechaDespacho);

                // Paso 6: cupo de la fecha (solo cuentan los envíos Programado)
                var programados = await repositorio.ContarProgramadosAsync(fechaDespacho, ct);
                if (programados >= cupoDiario)
                {
                    rechazo = new EnvioRechazado(MotivosRechazo.SinCupo, Fecha: fechaDespacho, CupoDiario: cupoDiario);
                }
                else
                {
                    var creado = await repositorio.CrearEnvioAsync(new NuevoEnvio(
                        comando.PedidoId, comando.SagaId, direccion!, fechaDespacho, fechaEntrega, ahora), ct);
                    programado = new EnvioProgramado(creado.EnvioId, creado.CodigoSeguimiento, fechaDespacho, fechaEntrega);
                }
            }

            // Paso 7: registrar el messageId y confirmar (misma transacción)
            await repositorio.RegistrarMensajeProcesadoAsync(comando.MessageId, ct);
            await repositorio.ConfirmarTransaccionAsync(ct);
        }
        catch
        {
            await repositorio.DeshacerTransaccionAsync(CancellationToken.None);
            throw;
        }

        // Paso 8: publicar el resultado DESPUÉS del commit
        if (programado is not null)
        {
            logger.LogInformation("Envío {EnvioId} programado para {Fecha} (pedido {PedidoId})",
                programado.EnvioId, programado.FechaDespacho, comando.PedidoId);
            await publisher.PublicarAsync(Mensaje.ResponderA(comando, programado, ahora), ct);
        }
        else
        {
            logger.LogInformation("Envío rechazado ({Motivo}) para el pedido {PedidoId}", rechazo!.Motivo, comando.PedidoId);
            await publisher.PublicarAsync(Mensaje.ResponderA(comando, rechazo!, ahora), ct);
        }
    }

    /// <summary>Nombre del primer campo vacío de la dirección, o null si está completa.</summary>
    private static string? BuscarCampoFaltante(DireccionEnvio? d)
    {
        if (d is null) return "direccion";
        if (string.IsNullOrWhiteSpace(d.Calle)) return "calle";
        if (string.IsNullOrWhiteSpace(d.Numero)) return "numero";
        if (string.IsNullOrWhiteSpace(d.Ciudad)) return "ciudad";
        if (string.IsNullOrWhiteSpace(d.CodigoPostal)) return "codigoPostal";
        return null;
    }
}
