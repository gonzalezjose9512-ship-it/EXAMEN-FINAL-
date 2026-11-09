using Dapper;
using Npgsql;
using TiendaNorte.Contracts;

namespace Inventario.Service.Datos;

/// <summary>
/// Lecturas para los GET de consulta. Solo leen: no escriben nada ni publican mensajes.
/// </summary>
public sealed class ConsultasInventario(NpgsqlDataSource dataSource)
{
    private const string SelectProductos = """
        SELECT producto_id AS ProductoId, nombre AS Nombre, precio AS Precio, stock_disponible AS StockDisponible
        FROM inventario.productos
        """;

    public async Task<IReadOnlyList<ProductoDto>> ListarProductosAsync(CancellationToken ct = default)
    {
        await using var conexion = await dataSource.OpenConnectionAsync(ct);
        var productos = await conexion.QueryAsync<ProductoDto>(
            new CommandDefinition(SelectProductos + " ORDER BY producto_id", cancellationToken: ct));
        return productos.ToList();
    }

    public async Task<ProductoDto?> ObtenerProductoAsync(string productoId, CancellationToken ct = default)
    {
        await using var conexion = await dataSource.OpenConnectionAsync(ct);
        return await conexion.QuerySingleOrDefaultAsync<ProductoDto>(
            new CommandDefinition(SelectProductos + " WHERE producto_id = @productoId", new { productoId }, cancellationToken: ct));
    }

    public async Task<ReservaDto?> ObtenerReservaAsync(string reservaId, CancellationToken ct = default)
    {
        await using var conexion = await dataSource.OpenConnectionAsync(ct);

        var reserva = await conexion.QuerySingleOrDefaultAsync<FilaReserva>(new CommandDefinition("""
            SELECT reserva_id AS ReservaId, pedido_id AS PedidoId, estado AS Estado, fecha_utc AS FechaUtc
            FROM inventario.reservas
            WHERE reserva_id = @reservaId
            """, new { reservaId }, cancellationToken: ct));
        if (reserva is null)
            return null;

        var items = await conexion.QueryAsync<(string ProductoId, int Cantidad)>(new CommandDefinition("""
            SELECT producto_id AS ProductoId, cantidad AS Cantidad
            FROM inventario.reserva_items
            WHERE reserva_id = @reservaId
            ORDER BY producto_id
            """, new { reservaId }, cancellationToken: ct));

        return new ReservaDto(
            reserva.ReservaId, reserva.PedidoId, reserva.Estado,
            items.Select(i => new ItemStock(i.ProductoId, i.Cantidad)).ToList(),
            reserva.FechaUtc);
    }

    private sealed class FilaReserva
    {
        public string ReservaId { get; set; } = string.Empty;
        public Guid PedidoId { get; set; }
        public string Estado { get; set; } = string.Empty;
        public DateTime FechaUtc { get; set; }
    }
}

/// <summary>{ "productoId", "nombre", "precio", "stockDisponible" }</summary>
public sealed class ProductoDto
{
    public string ProductoId { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public int StockDisponible { get; set; }
}

/// <summary>{ "reservaId", "pedidoId", "estado", "items", "fechaUtc" }</summary>
public sealed record ReservaDto(string ReservaId, Guid PedidoId, string Estado, IReadOnlyList<ItemStock> Items, DateTime FechaUtc);
