using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using TiendaNorte.Contracts;

namespace Inventario.Service.Mensajeria;

/// <summary>
/// Una única conexión a RabbitMQ por proceso. Cada canal nuevo declara la topología completa:
/// exchange tiendanorte (direct, durable) y las 4 colas durables con su binding.
/// </summary>
public sealed class ConexionRabbitMq(IOptions<OpcionesRabbitMq> opciones, ILogger<ConexionRabbitMq> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _candado = new(1, 1);
    private IConnection? _conexion;

    public async Task<IChannel> CrearCanalAsync(CancellationToken ct = default)
    {
        var conexion = await ObtenerConexionAsync(ct);
        var canal = await conexion.CreateChannelAsync(cancellationToken: ct);
        await DeclararTopologiaAsync(canal, ct);
        return canal;
    }

    private async Task<IConnection> ObtenerConexionAsync(CancellationToken ct)
    {
        if (_conexion is { IsOpen: true })
            return _conexion;

        await _candado.WaitAsync(ct);
        try
        {
            if (_conexion is { IsOpen: true })
                return _conexion;

            var o = opciones.Value;
            var fabrica = new ConnectionFactory
            {
                HostName = o.Host,
                Port = o.Port,
                UserName = o.Usuario,
                Password = o.Clave,
                AutomaticRecoveryEnabled = true
            };

            for (var intento = 1; ; intento++)
            {
                try
                {
                    _conexion = await fabrica.CreateConnectionAsync(ct);
                    logger.LogInformation("Conectado a RabbitMQ en {Host}:{Port}", o.Host, o.Port);
                    return _conexion;
                }
                catch (BrokerUnreachableException) when (intento < 10)
                {
                    logger.LogWarning("RabbitMQ no responde (intento {Intento}/10). Reintentando en 3 segundos...", intento);
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }
        }
        finally
        {
            _candado.Release();
        }
    }

    private static async Task DeclararTopologiaAsync(IChannel canal, CancellationToken ct)
    {
        await canal.ExchangeDeclareAsync(
            exchange: Topologia.Exchange, type: ExchangeType.Direct,
            durable: true, autoDelete: false, arguments: null, cancellationToken: ct);

        foreach (var cola in Topologia.TodasLasColas)
        {
            await canal.QueueDeclareAsync(
                queue: cola, durable: true, exclusive: false, autoDelete: false,
                arguments: null, cancellationToken: ct);

            await canal.QueueBindAsync(
                queue: cola, exchange: Topologia.Exchange, routingKey: cola,
                arguments: null, cancellationToken: ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conexion is not null)
            await _conexion.DisposeAsync();
        _candado.Dispose();
    }
}
