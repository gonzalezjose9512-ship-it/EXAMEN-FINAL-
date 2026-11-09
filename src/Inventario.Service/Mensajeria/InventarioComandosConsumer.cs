using System.Text.Json;
using Inventario.Service.Handlers;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TiendaNorte.Contracts;

namespace Inventario.Service.Mensajeria;

/// <summary>
/// Consume la cola inventario.comandos (BackgroundService).
/// Un mensaje por vez (prefetch 1) y confirmación manual (autoAck: false):
///   - procesado bien o rechazo de negocio → ack
///   - JSON inválido o tipo desconocido   → log + ack (se descarta)
///   - error técnico                       → nack con reencolado (se reintenta)
/// </summary>
public sealed class InventarioComandosConsumer(
    ConexionRabbitMq conexion,
    IServiceScopeFactory scopes,
    ILogger<InventarioComandosConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Paso 1: declarar la cola (durable) y consumir de a un mensaje con confirmación manual
        var canal = await conexion.CrearCanalAsync(stoppingToken);
        await canal.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) => ProcesarAsync(canal, entrega, stoppingToken);

        await canal.BasicConsumeAsync(Topologia.ColaInventario, autoAck: false, consumer: consumidor, cancellationToken: stoppingToken);
        logger.LogInformation("Escuchando la cola {Cola}", Topologia.ColaInventario);

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
            // Paso 2: deserializar el sobre y despachar según "tipo"
            await using var scope = scopes.CreateAsyncScope();
            var servicios = scope.ServiceProvider;

            var procesado = LeerTipo(entrega.Body) switch
            {
                nameof(ReservarStock) when Leer<ReservarStock>(entrega.Body) is { Datos.Items: not null } m
                    => await Ejecutar(servicios.GetRequiredService<ReservarStockHandler>().ManejarAsync(m, ct)),
                nameof(LiberarStock) when Leer<LiberarStock>(entrega.Body) is { Datos.ReservaId: not null } m
                    => await Ejecutar(servicios.GetRequiredService<LiberarStockHandler>().ManejarAsync(m, ct)),
                _ => false
            };

            if (!procesado)
                logger.LogWarning("Mensaje descartado: JSON inválido o tipo desconocido. Contenido: {Contenido}",
                    System.Text.Encoding.UTF8.GetString(entrega.Body.Span));

            // Paso 9: ack (también para los descartados, así no vuelven a la cola)
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Error técnico: el handler ya hizo rollback; el mensaje vuelve a la cola para reintentarse
            logger.LogError(ex, "Error técnico procesando un mensaje de {Cola}. Se reencola.", Topologia.ColaInventario);
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
