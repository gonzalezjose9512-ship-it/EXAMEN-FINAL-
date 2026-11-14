using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pedidos.Application.Puertos;
using Pedidos.Application.Sagas;
using Pedidos.Domain.Excepciones;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TiendaNorte.Contracts;

namespace Pedidos.Infrastructure.Mensajeria;

/// <summary>
/// Adaptador de ENTRADA de mensajería: consume la cola pedidos.respuestas y llama a la saga.
/// Es la parte de Pedidos que hace avanzar la saga. Se registra en Pedidos.Api con
/// builder.Services.AddHostedService&lt;RespuestasConsumer&gt;().
///
/// Un mensaje por vez (prefetch 1) y confirmación manual (autoAck: false):
///   - procesado, saga inexistente o evento inesperado → ack
///   - JSON inválido o tipo desconocido                → log + ack (se descarta)
///   - messageId ya procesado                          → ack sin repetir la acción
///   - error técnico                                   → nack con reencolado
/// </summary>
public sealed class RespuestasConsumer(
    ConexionRabbitMq conexion,
    IServiceScopeFactory scopes,
    ILogger<RespuestasConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Paso 1: declarar la cola (durable), un mensaje por vez y confirmación manual
        var canal = await conexion.CrearCanalAsync(stoppingToken);
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) => ProcesarAsync(canal, entrega, stoppingToken);

        await canal.BasicConsumeAsync(Topologia.ColaRespuestas, autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);
        logger.LogInformation("Escuchando la cola {Cola}", Topologia.ColaRespuestas);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // La API se está deteniendo
        }
    }

    private async Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        try
        {
            // Un scope por mensaje: la unidad de trabajo (conexión y transacción) es de este mensaje
            await using var scope = scopes.CreateAsyncScope();
            var servicios = scope.ServiceProvider;
            var cuerpo = entrega.Body;

            // Paso 2: deserializar el sobre según "tipo"
            var resultado = LeerTipo(cuerpo) switch
            {
                nameof(StockReservado) => await ManejarAsync<StockReservado>(servicios, cuerpo, ct),
                nameof(StockRechazado) => await ManejarAsync<StockRechazado>(servicios, cuerpo, ct),
                nameof(StockLiberado) => await ManejarAsync<StockLiberado>(servicios, cuerpo, ct),
                nameof(PagoAprobado) => await ManejarAsync<PagoAprobado>(servicios, cuerpo, ct),
                nameof(PagoRechazado) => await ManejarAsync<PagoRechazado>(servicios, cuerpo, ct),
                nameof(PagoReembolsado) => await ManejarAsync<PagoReembolsado>(servicios, cuerpo, ct),
                nameof(EnvioProgramado) => await ManejarAsync<EnvioProgramado>(servicios, cuerpo, ct),
                nameof(EnvioRechazado) => await ManejarAsync<EnvioRechazado>(servicios, cuerpo, ct),
                nameof(EnvioAnulado) => await ManejarAsync<EnvioAnulado>(servicios, cuerpo, ct),
                _ => null
            };

            if (resultado is null)
                logger.LogWarning("Mensaje descartado: JSON inválido o tipo desconocido. Contenido: {Contenido}",
                    System.Text.Encoding.UTF8.GetString(cuerpo.Span));
            else if (resultado.Tipo == TipoResultadoSaga.Procesado)
                logger.LogInformation("{Detalle}", resultado.Detalle);
            else
                logger.LogWarning("Evento ignorado: {Detalle}", resultado.Detalle);

            // Paso 8: ack
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (EstadoInvalidoException ex)
        {
            // El pedido no estaba PendienteConfirmacion: no es un error técnico.
            // Reencolarlo lo haría fallar para siempre, por eso se registra y se descarta.
            logger.LogWarning(ex, "Evento descartado: {Detalle}", ex.Message);
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Error técnico: la saga ya hizo rollback; el mensaje vuelve a la cola para reintentarse
            logger.LogError(ex, "Error técnico procesando un mensaje de {Cola}. Se reencola.", Topologia.ColaRespuestas);
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Pasos 3 y 4: idempotencia y llamada a la saga. Devuelve null si el mensaje está mal formado.
    /// </summary>
    private static async Task<ResultadoSaga?> ManejarAsync<TEvento>(
        IServiceProvider servicios, ReadOnlyMemory<byte> cuerpo, CancellationToken ct)
        where TEvento : notnull
    {
        Mensaje<TEvento>? evento;
        try
        {
            evento = JsonSerializer.Deserialize<Mensaje<TEvento>>(cuerpo.Span, JsonContratos.Opciones);
        }
        catch (JsonException)
        {
            return null;
        }

        if (evento is null || evento.Datos is null || evento.SagaId == Guid.Empty)
            return null;

        // Paso 3: idempotencia. Si el messageId ya se procesó, ack sin repetir la acción
        var sagaRepository = servicios.GetRequiredService<ISagaRepository>();
        if (await sagaRepository.MensajeYaProcesadoAsync(evento.MessageId, ct))
            return ResultadoSaga.EventoInesperado($"El mensaje {evento.MessageId} ({evento.Tipo}) ya fue procesado.");

        var saga = servicios.GetRequiredService<ConfirmacionPedidoSaga>();
        return await saga.ManejarAsync(evento, ct);
    }

    private static string? LeerTipo(ReadOnlyMemory<byte> cuerpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            return documento.RootElement.TryGetProperty("tipo", out var tipo) ? tipo.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
