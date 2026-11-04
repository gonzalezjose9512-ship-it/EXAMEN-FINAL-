using TiendaNorte.Contracts;

namespace Inventario.Service.Abstracciones;

/// <summary>
/// Publica los eventos de Inventario en pedidos.respuestas. Lo implementa RespuestaPublisher con RabbitMQ.Client.
/// </summary>
public interface IRespuestaPublisher
{
    Task PublicarAsync<TDatos>(Mensaje<TDatos> mensaje, CancellationToken ct = default);
}
