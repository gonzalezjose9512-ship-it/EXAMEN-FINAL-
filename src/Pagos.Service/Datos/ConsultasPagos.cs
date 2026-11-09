using Dapper;
using Npgsql;

namespace Pagos.Service.Datos;

/// <summary>Lecturas de pagos.pagos para los GET. No escriben nada ni publican mensajes.</summary>
public sealed class ConsultasPagos(NpgsqlDataSource dataSource)
{
    // Nunca se expone el número completo: solo existe tarjeta_ultimos4
    private const string SelectPagos = """
        SELECT pago_id AS PagoId, pedido_id AS PedidoId, monto AS Monto, estado AS Estado, motivo AS Motivo,
               tarjeta_ultimos4 AS TarjetaUltimos4, codigo_autorizacion AS CodigoAutorizacion,
               fecha_utc AS FechaUtc, fecha_reembolso AS FechaReembolso
        FROM pagos.pagos
        """;

    public async Task<PagoDto?> ObtenerAsync(string pagoId, CancellationToken ct = default)
    {
        await using var conexion = await dataSource.OpenConnectionAsync(ct);
        return await conexion.QuerySingleOrDefaultAsync<PagoDto>(new CommandDefinition(
            SelectPagos + " WHERE pago_id = @pagoId", new { pagoId }, cancellationToken: ct));
    }

    /// <summary>Intentos de pago del pedido, del más reciente al más antiguo.</summary>
    public async Task<IReadOnlyList<PagoDto>> ListarPorPedidoAsync(Guid pedidoId, CancellationToken ct = default)
    {
        await using var conexion = await dataSource.OpenConnectionAsync(ct);
        var pagos = await conexion.QueryAsync<PagoDto>(new CommandDefinition(
            SelectPagos + " WHERE pedido_id = @pedidoId ORDER BY fecha_utc DESC", new { pedidoId }, cancellationToken: ct));
        return pagos.ToList();
    }
}

/// <summary>{ "pagoId", "pedidoId", "monto", "estado", "tarjetaUltimos4", "codigoAutorizacion", "fechaUtc", ... }</summary>
public sealed class PagoDto
{
    public string PagoId { get; set; } = string.Empty;
    public Guid PedidoId { get; set; }
    public decimal Monto { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public string? TarjetaUltimos4 { get; set; }
    public string? CodigoAutorizacion { get; set; }
    public DateTime FechaUtc { get; set; }
    public DateTime? FechaReembolso { get; set; }
}
