using Pedidos.Application.Puertos;
using Pedidos.Domain;

namespace Pedidos.Application.CasosDeUso;

/// <summary>
/// Endpoint 1 - POST /api/pedidos.
/// Crea un pedido vacío en Borrador y guarda el evento PedidoCreado.
/// No crea la saga ni publica mensajes.
/// </summary>
public sealed class CrearPedido(IEventStore eventStore)
{
    public async Task<Pedido> EjecutarAsync(string clienteId, Direccion direccion, CancellationToken ct = default)
    {
        var pedidoId = Guid.NewGuid();

        var pedido = Pedido.Crear(pedidoId, clienteId, direccion);

        // Pedido nuevo: la versión esperada es 0 (todavía no hay eventos)
        await eventStore.GuardarAsync(pedidoId, versionEsperada: 0, pedido.EventosNuevos, ct);

        return pedido;
    }
}
