using TiendaNorte.Contracts;

namespace Pedidos.Application.Puertos;

/// <summary>
/// Puerto de salida: publicación de comandos hacia los servicios.
/// Lo implementa RabbitMqPublisher en Pedidos.Infrastructure (exchange tiendanorte,
/// routing key = nombre de la cola, mensaje persistente).
/// </summary>
public interface IMessagePublisher
{
    /// <summary>
    /// Publica el mensaje en la cola indicada. Ejemplo:
    /// PublicarAsync(Topologia.ColaInventario, mensajeReservarStock).
    /// </summary>
    Task PublicarAsync<TDatos>(string cola, Mensaje<TDatos> mensaje, CancellationToken ct = default);
}
