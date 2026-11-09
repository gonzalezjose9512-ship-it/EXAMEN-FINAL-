using System.Text.Json;
using Pagos.Service.Handlers;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TiendaNorte.Contracts;

namespace Pagos.Service.Mensajeria;

/// <summary>
/// Consume la cola pagos.comandos (BackgroundService).
/// Un mensaje por vez (prefetch 1) y confirmación manual (autoAck: false):
///   - procesado bien o rechazo de negocio → ack
///   - JSON inválido o tipo desconocido   → log + ack (se descarta)
///   - error técnico                       → nack con reencolado (se reintenta)
/// Si el servicio está apagado, los comandos esperan en la cola (escenario E5).
/// </summary>
public sealed class PagosComandosConsumer(
    ConexionRabbitMq conexion,
    IServiceScopeFactory scopes,
    ILogger<PagosComandosConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var canal = await conexion.CrearCanalAsync(stoppingToken);
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) => ProcesarAsync(canal, entrega, stoppingToken);

        await canal.BasicConsumeAsync(Topologia.ColaPagos, autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);
        logger.LogInformation("Escuchando la cola {Cola}", Topologia.ColaPagos);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // El servicio se está deteniendo
        }
    }

    private async Task ProcesarAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var servicios = scope.ServiceProvider;

            var procesado = LeerTipo(entrega.Body) switch
            {
                nameof(Cobrar) when Leer<Cobrar>(entrega.Body) is { Datos.Tarjeta: not null } m
                    => await Ejecutar(servicios.GetRequiredService<CobrarHandler>().ManejarAsync(m, ct)),
                nameof(Reembolsar) when Leer<Reembolsar>(entrega.Body) is { Datos.PagoId: not null } m
                    => await Ejecutar(servicios.GetRequiredService<ReembolsarHandler>().ManejarAsync(m, ct)),
                _ => false
            };

            if (!procesado)
                logger.LogWarning("Mensaje descartado: JSON inválido o tipo desconocido. Contenido: {Contenido}",
                    System.Text.Encoding.UTF8.GetString(entrega.Body.Span));

            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error técnico procesando un mensaje de {Cola}. Se reencola.", Topologia.ColaPagos);
            await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            await canal.BasicNackAsync(entrega.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
        }
    }

    private static async Task<bool> Ejecutar(Task tarea)
    {
        await tarea;
        return true;
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

    private static Mensaje<T>? Leer<T>(ReadOnlyMemory<byte> cuerpo)
    {
        try
        {
            return JsonSerializer.Deserialize<Mensaje<T>>(cuerpo.Span, JsonContratos.Opciones);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
