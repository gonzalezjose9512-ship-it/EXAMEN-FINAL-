using Dapper;
using Npgsql;
using TiendaNorte.Contracts;

namespace Logistica.Service.Datos;

/// <summary>Lecturas de logistica.envios para los GET. No escriben nada ni publican mensajes.</summary>
public sealed class ConsultasLogistica(NpgsqlDataSource dataSource)
{
    private const string SelectEnvios = """
        SELECT envio_id AS EnvioId, pedido_id AS PedidoId, estado AS Estado,
               codigo_seguimiento AS CodigoSeguimiento,
               fecha_despacho AS FechaDespacho, fecha_estimada_entrega AS FechaEstimadaEntrega,
               calle AS Calle, numero AS Numero, ciudad AS Ciudad, codigo_postal AS CodigoPostal
        FROM logistica.envios
        """;

    public async Task<EnvioDto?> ObtenerAsync(string envioId, CancellationToken ct = default)
    {
        await using var conexion = await dataSource.OpenConnectionAsync(ct);
        var fila = await conexion.QuerySingleOrDefaultAsync<FilaEnvio>(new CommandDefinition(
            SelectEnvios + " WHERE envio_id = @envioId", new { envioId }, cancellationToken: ct));
        return fila?.ADto();
    }

    public async Task<IReadOnlyList<EnvioDto>> ListarPorFechaAsync(DateOnly fecha, CancellationToken ct = default)
    {
        await using var conexion = await dataSource.OpenConnectionAsync(ct);
        var filas = await conexion.QueryAsync<FilaEnvio>(new CommandDefinition(
            SelectEnvios + " WHERE fecha_despacho = CAST(@fecha AS date) ORDER BY envio_id",
            new { fecha = fecha.ToDateTime(TimeOnly.MinValue) }, cancellationToken: ct));
        return filas.Select(f => f.ADto()).ToList();
    }

    private sealed class FilaEnvio
    {
        public string EnvioId { get; set; } = string.Empty;
        public Guid PedidoId { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string CodigoSeguimiento { get; set; } = string.Empty;
        public DateTime FechaDespacho { get; set; }
        public DateTime FechaEstimadaEntrega { get; set; }
        public string Calle { get; set; } = string.Empty;
        public string Numero { get; set; } = string.Empty;
        public string Ciudad { get; set; } = string.Empty;
        public string CodigoPostal { get; set; } = string.Empty;

        public EnvioDto ADto() => new(
            EnvioId, PedidoId, Estado, CodigoSeguimiento,
            DateOnly.FromDateTime(FechaDespacho), DateOnly.FromDateTime(FechaEstimadaEntrega),
            new DireccionEnvio(Calle, Numero, Ciudad, CodigoPostal));
    }
}

/// <summary>{ "envioId", "pedidoId", "estado", "codigoSeguimiento", "fechaDespacho", "fechaEstimadaEntrega", "direccion" }</summary>
public sealed record EnvioDto(
    string EnvioId, Guid PedidoId, string Estado, string CodigoSeguimiento,
    DateOnly FechaDespacho, DateOnly FechaEstimadaEntrega, DireccionEnvio Direccion);

/// <summary>{ "fecha", "cupoDiario", "programados", "envios" }</summary>
public sealed record EnviosDelDiaDto(DateOnly Fecha, int CupoDiario, int Programados, IReadOnlyList<EnvioDto> Envios);
