using System.Text.Json;
using Logistica.Service.Abstracciones;
using RabbitMQ.Client;
using TiendaNorte.Contracts;

namespace Logistica.Service.Mensajeria;

/// <summary>
/// Publica los eventos de Logística en el exchange tiendanorte con routing key pedidos.respuestas,
/// como mensaje persistente en JSON camelCase.
/// </summary>
public sealed class RespuestaPublisher(ConexionRabbitMq conexion) : IRespuestaPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _candado = new(1, 1);
    private IChannel? _canal;

    public async Task PublicarAsync<TDatos>(Mensaje<TDatos> mensaje, CancellationToken ct = default)
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
                routingKey: Topologia.ColaRespuestas,
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
