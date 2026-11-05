using Pedidos.Application.Puertos;
using Pedidos.Domain;

namespace Pedidos.Application.CasosDeUso;

/// <summary>
/// Endpoint 4 - GET /api/pedidos/{pedidoId}.
/// Reconstruye el estado aplicando los eventos. No lee ninguna tabla de estado,
/// no escribe ni publica.
/// </summary>
public sealed class ObtenerPedido(IEventStore eventStore)
{
    public Task<Pedido> EjecutarAsync(Guid pedidoId, CancellationToken ct = default) =>
        eventStore.CargarPedidoAsync(pedidoId, ct);
}
