using Pedidos.Domain.Eventos;

namespace Pedidos.Application.Puertos;

/// <summary>
/// Puerto de salida: el almacén de eventos del pedido (tabla pedidos.eventos).
/// Lo implementa PostgresEventStore en Pedidos.Infrastructure.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Lee los eventos del pedido ordenados por versión y los convierte al record que indica tipo_evento.
    /// Devuelve una lista vacía si el pedido no existe (el caso de uso lanza NoEncontradoException).
    /// </summary>
    Task<IReadOnlyList<EventoPedido>> LeerAsync(Guid pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Lee las filas tal como están guardadas, sin rehidratar el agregado (endpoint 5: GET /eventos).
    /// Devuelve una lista vacía si el pedido no existe.
    /// </summary>
    Task<IReadOnlyList<EventoGuardado>> LeerFilasAsync(Guid pedidoId, CancellationToken ct = default);

    /// <summary>
    /// Inserta los eventos nuevos del agregado. versionEsperada es la versión que tenía el pedido
    /// al leerlo (0 si es nuevo). Si otro proceso ya guardó esa versión, lanza ConcurrenciaException.
    /// </summary>
    Task GuardarAsync(Guid pedidoId, int versionEsperada, IReadOnlyList<EventoPedido> eventosNuevos, CancellationToken ct = default);
}

/// <summary>
/// Una fila de pedidos.eventos tal como está guardada. DatosJson es el contenido de la columna datos (jsonb).
/// </summary>
public sealed record EventoGuardado(int Version, string Tipo, DateTime FechaUtc, string DatosJson);
