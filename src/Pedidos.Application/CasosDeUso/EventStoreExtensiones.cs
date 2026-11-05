using Pedidos.Application.Excepciones;
using Pedidos.Application.Puertos;
using Pedidos.Domain;

namespace Pedidos.Application.CasosDeUso;

/// <summary>
/// Paso común de varios casos de uso: leer los eventos del pedido y rehidratarlo.
/// </summary>
public static class EventStoreExtensiones
{
    /// <summary>
    /// Lee los eventos del pedido y lo reconstruye con Pedido.Rehidratar.
    /// Si no hay eventos, el pedido no existe: NoEncontradoException (404).
    /// </summary>
    public static async Task<Pedido> CargarPedidoAsync(this IEventStore eventStore, Guid pedidoId, CancellationToken ct = default)
    {
        var eventos = await eventStore.LeerAsync(pedidoId, ct);
        if (eventos.Count == 0)
            throw new NoEncontradoException($"No existe el pedido {pedidoId}.");

        return Pedido.Rehidratar(eventos);
    }
}
