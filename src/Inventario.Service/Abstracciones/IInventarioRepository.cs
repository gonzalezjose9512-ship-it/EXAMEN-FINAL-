using TiendaNorte.Contracts;

namespace Inventario.Service.Abstracciones;

/// <summary>
/// Acceso a los datos del esquema inventario. Lo implementa InventarioRepository con Npgsql.
/// Todas las operaciones se ejecutan dentro de la transacción abierta con IniciarTransaccionAsync.
/// Los handlers dependen de esta interfaz para que las pruebas usen un doble sin PostgreSQL.
/// </summary>
public interface IInventarioRepository
{
    // ----- Transacción -----
    Task IniciarTransaccionAsync(CancellationToken ct = default);
    Task ConfirmarTransaccionAsync(CancellationToken ct = default);
    Task DeshacerTransaccionAsync(CancellationToken ct = default);

    // ----- Idempotencia (inventario.mensajes_procesados) -----
    Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default);
    Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default);

    // ----- ReservarStock -----

    /// <summary>
    /// SELECT producto_id, stock_disponible FROM inventario.productos WHERE producto_id = ANY(@ids) FOR UPDATE.
    /// Devuelve el stock de los productos que existen (los que no existen no aparecen en el diccionario).
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> ObtenerStockBloqueandoAsync(IReadOnlyCollection<string> productoIds, CancellationToken ct = default);

    /// <summary>
    /// Descuenta el stock de cada ítem, inserta la reserva 'Activa' y sus ítems.
    /// Devuelve el reservaId generado con la secuencia (por ejemplo R-000015).
    /// </summary>
    Task<string> CrearReservaAsync(Guid pedidoId, Guid sagaId, IReadOnlyList<ItemStock> items, CancellationToken ct = default);

    // ----- LiberarStock -----

    /// <summary>SELECT estado FROM inventario.reservas WHERE reserva_id = @id FOR UPDATE. Null si no existe.</summary>
    Task<string?> ObtenerEstadoReservaBloqueandoAsync(string reservaId, CancellationToken ct = default);

    /// <summary>Devuelve al stock las cantidades de inventario.reserva_items y marca la reserva como 'Liberada'.</summary>
    Task LiberarReservaAsync(string reservaId, CancellationToken ct = default);
}
