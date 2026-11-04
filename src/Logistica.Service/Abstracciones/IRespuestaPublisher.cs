using TiendaNorte.Contracts;

namespace Logistica.Service.Abstracciones;

/// <summary>
/// Publica los eventos de Logística en pedidos.respuestas. Lo implementa RespuestaPublisher con RabbitMQ.Client.
/// </summary>
public interface IRespuestaPublisher
{
    Task PublicarAsync<TDatos>(Mensaje<TDatos> mensaje, CancellationToken ct = default);
}
