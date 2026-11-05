using Pedidos.Application.Excepciones;
using Pedidos.Application.Puertos;

namespace Pedidos.Application.CasosDeUso;

/// <summary>
/// Endpoint 5 - GET /api/pedidos/{pedidoId}/eventos.
/// Devuelve los eventos tal como están guardados, sin rehidratar el agregado.
/// Sirve para demostrar el event sourcing.
/// </summary>
public sealed class ObtenerEventos(IEventStore eventStore)
{
    public async Task<IReadOnlyList<EventoGuardado>> EjecutarAsync(Guid pedidoId, CancellationToken ct = default)
    {
        var filas = await eventStore.LeerFilasAsync(pedidoId, ct);
        if (filas.Count == 0)
            throw new NoEncontradoException($"No existe el pedido {pedidoId}.");

        return filas;
    }
}
