using Pedidos.Application.Puertos;
using Pedidos.Domain;

namespace Pedidos.Application.CasosDeUso;

/// <summary>
/// Endpoint 2 - POST /api/pedidos/{pedidoId}/items.
/// Lee y rehidrata el pedido, el dominio valida y genera ItemAgregado, y se guarda.
/// No publica mensajes.
/// </summary>
public sealed class AgregarItem(IEventStore eventStore)
{
    public async Task<Pedido> EjecutarAsync(Guid pedidoId, string productoId, int cantidad, decimal precioUnitario, CancellationToken ct = default)
    {
        var pedido = await eventStore.CargarPedidoAsync(pedidoId, ct);

        // Versión que tenía el pedido al leerlo: si otro proceso guarda antes, hay conflicto (409)
        var versionLeida = pedido.Version;

        pedido.AgregarItem(productoId, cantidad, precioUnitario);

        await eventStore.GuardarAsync(pedidoId, versionLeida, pedido.EventosNuevos, ct);

        return pedido;
    }
}
