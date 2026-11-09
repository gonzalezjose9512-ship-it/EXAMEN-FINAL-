using System.Text.Json;
using Pedidos.Application.Puertos;
using RabbitMQ.Client;
using TiendaNorte.Contracts;

namespace Pedidos.Infrastructure.Mensajeria;

/// <summary>
/// Adaptador de salida: implementa IMessagePublisher con RabbitMQ.Client.
/// Publica en el exchange tiendanorte con routing key = nombre de la cola,
/// como mensaje PERSISTENTE (sobrevive a un reinicio de RabbitMQ) y en JSON camelCase.
/// </summary>
public sealed class RabbitMqPublisher(ConexionRabbitMq conexion) : IMessagePublisher, IAsyncDisposable
{
    // Un canal no admite publicaciones simultáneas: el candado las ordena
    private readonly SemaphoreSlim _candado = new(1, 1);
    private IChannel? _canal;

    public async Task PublicarAsync<TDatos>(string cola, Mensaje<TDatos> mensaje, CancellationToken ct = default)
    {
        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(mensaje, JsonContratos.Opciones);
        var propiedades = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = mensaje.MessageId.ToString(),
            Type = mensaje.Tipo
        };

        await _candado.WaitAsync(ct);
        try
        {
            if (_canal is not { IsOpen: true })
                _canal = await conexion.CrearCanalAsync(ct);

            await _canal.BasicPublishAsync(
                exchange: Topologia.Exchange,
                routingKey: cola,
                mandatory: false,
                basicProperties: propiedades,
                body: cuerpo,
                cancellationToken: ct);
        }
        finally
        {
            _candado.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_canal is not null)
            await _canal.DisposeAsync();
        _candado.Dispose();
    }
}
