namespace Pedidos.Domain.Eventos;

/// <summary>
/// Base de todos los eventos del pedido (Hito 2 - Event Sourcing).
/// Cada evento es un record con su número de versión y su fecha UTC.
///
/// PedidoId, Version y FechaUtc son METADATOS del evento: se guardan en las columnas
/// stream_id, version y fecha_utc de pedidos.eventos. El resto de las propiedades
/// (las de cada evento concreto) es el CONTENIDO y se guarda en la columna datos (jsonb).
/// </summary>
public abstract record EventoPedido
{
    /// <summary>Id del pedido al que pertenece el evento (stream_id).</summary>
    public Guid PedidoId { get; init; }

    /// <summary>Número de versión del evento dentro del stream del pedido (1, 2, 3...).</summary>
    public int Version { get; init; }

    /// <summary>Momento en que ocurrió el evento, en UTC.</summary>
    public DateTime FechaUtc { get; init; }
}
