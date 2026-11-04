namespace Pedidos.Domain.Eventos;

/// <summary>
/// Base de todos los eventos del pedido (Hito 2 - Event Sourcing).
/// Cada evento es un record con su número de versión y su fecha UTC.
/// La versión es la posición del evento en el historial del pedido (1, 2, 3...).
/// </summary>
public abstract record EventoPedido
{
    /// <summary>Número de versión del evento dentro del stream del pedido.</summary>
    public int Version { get; init; }

    /// <summary>Momento en que ocurrió el evento, en UTC.</summary>
    public DateTime FechaUtc { get; init; }
}
