using Dapper;
using Inventario.Service.Abstracciones;
using Npgsql;
using TiendaNorte.Contracts;

namespace Inventario.Service.Datos;

/// <summary>
/// Implementa IInventarioRepository con Npgsql + Dapper sobre el esquema inventario.
/// Una conexión y una transacción por mensaje procesado (vida scoped).
/// </summary>
public sealed class InventarioRepository(NpgsqlDataSource dataSource) : IInventarioRepository, IAsyncDisposable
{
    private NpgsqlConnection? _conexion;
    private NpgsqlTransaction? _transaccion;

    private NpgsqlConnection Conexion =>
        _conexion ?? throw new InvalidOperationException("Llamá primero a IniciarTransaccionAsync.");

    // ----- Transacción -----

    public async Task IniciarTransaccionAsync(CancellationToken ct = default)
    {
        _conexion ??= await dataSource.OpenConnectionAsync(ct);
        _transaccion = await _conexion.BeginTransactionAsync(ct);
    }

    public async Task ConfirmarTransaccionAsync(CancellationToken ct = default)
    {
        if (_transaccion is null)
            throw new InvalidOperationException("No hay una transacción abierta.");
        await _transaccion.CommitAsync(ct);
        await _transaccion.DisposeAsync();
        _transaccion = null;
    }

    public async Task DeshacerTransaccionAsync(CancellationToken ct = default)
    {
        if (_transaccion is null)
            return;
        await _transaccion.RollbackAsync(ct);
        await _transaccion.DisposeAsync();
        _transaccion = null;
    }

    // ----- Idempotencia -----

    public Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Conexion.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM inventario.mensajes_procesados WHERE message_id = @messageId)",
            new { messageId }, _transaccion, cancellationToken: ct));

    public Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Conexion.ExecuteAsync(new CommandDefinition(
            "INSERT INTO inventario.mensajes_procesados (message_id, fecha_utc) VALUES (@messageId, now())",
            new { messageId }, _transaccion, cancellationToken: ct));

    // ----- ReservarStock -----

    public async Task<IReadOnlyDictionary<string, int>> ObtenerStockBloqueandoAsync(
        IReadOnlyCollection<string> productoIds, CancellationToken ct = default)
    {
        const string sql = """
            SELECT producto_id AS ProductoId, stock_disponible AS StockDisponible
            FROM inventario.productos
            WHERE producto_id = ANY(@ids)
            FOR UPDATE
            """;

        var filas = await Conexion.QueryAsync<(string ProductoId, int StockDisponible)>(new CommandDefinition(
            sql, new { ids = productoIds.ToArray() }, _transaccion, cancellationToken: ct));

        return filas.ToDictionary(f => f.ProductoId, f => f.StockDisponible);
    }

    public async Task<string> CrearReservaAsync(Guid pedidoId, Guid sagaId, IReadOnlyList<ItemStock> items, CancellationToken ct = default)
    {
        // Id con prefijo desde la secuencia: R-000001
        var reservaId = await Conexion.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT 'R-' || lpad(nextval('inventario.reservas_seq')::text, 6, '0')",
            transaction: _transaccion, cancellationToken: ct));

        await Conexion.ExecuteAsync(new CommandDefinition("""
            INSERT INTO inventario.reservas (reserva_id, pedido_id, saga_id, estado, fecha_utc)
            VALUES (@reservaId, @pedidoId, @sagaId, 'Activa', now())
            """, new { reservaId, pedidoId, sagaId }, _transaccion, cancellationToken: ct));

        foreach (var item in items)
        {
            await Conexion.ExecuteAsync(new CommandDefinition("""
                UPDATE inventario.productos
                SET stock_disponible = stock_disponible - @cantidad
                WHERE producto_id = @productoId
                """, new { cantidad = item.Cantidad, productoId = item.ProductoId }, _transaccion, cancellationToken: ct));

            await Conexion.ExecuteAsync(new CommandDefinition("""
                INSERT INTO inventario.reserva_items (reserva_id, producto_id, cantidad)
                VALUES (@reservaId, @productoId, @cantidad)
                """, new { reservaId, productoId = item.ProductoId, cantidad = item.Cantidad }, _transaccion, cancellationToken: ct));
        }

        return reservaId!;
    }

    // ----- LiberarStock -----

    public Task<string?> ObtenerEstadoReservaBloqueandoAsync(string reservaId, CancellationToken ct = default) =>
        Conexion.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT estado FROM inventario.reservas WHERE reserva_id = @reservaId FOR UPDATE",
            new { reservaId }, _transaccion, cancellationToken: ct));

    public async Task LiberarReservaAsync(string reservaId, CancellationToken ct = default)
    {
        // Por cada fila de reserva_items: stock_disponible = stock_disponible + cantidad
        await Conexion.ExecuteAsync(new CommandDefinition("""
            UPDATE inventario.productos p
            SET stock_disponible = p.stock_disponible + ri.cantidad
            FROM inventario.reserva_items ri
            WHERE ri.reserva_id = @reservaId AND p.producto_id = ri.producto_id
            """, new { reservaId }, _transaccion, cancellationToken: ct));

        await Conexion.ExecuteAsync(new CommandDefinition(
            "UPDATE inventario.reservas SET estado = 'Liberada' WHERE reserva_id = @reservaId",
            new { reservaId }, _transaccion, cancellationToken: ct));
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaccion is not null)
            await _transaccion.DisposeAsync();
        if (_conexion is not null)
            await _conexion.DisposeAsync();
    }
}
