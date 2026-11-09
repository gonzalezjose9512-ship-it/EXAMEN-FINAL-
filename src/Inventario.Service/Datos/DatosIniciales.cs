using Dapper;
using Npgsql;

namespace Inventario.Service.Datos;

/// <summary>
/// Carga al arrancar los 5 productos obligatorios del enunciado.
/// ON CONFLICT DO NOTHING: si ya existen, NO se pisa su stock (así se conserva
/// la evidencia de las reservas y compensaciones entre reinicios).
/// El script database/init/02_inventario.sql hace lo mismo al crear la base.
/// </summary>
public static class DatosIniciales
{
    public static async Task CargarAsync(IServiceProvider servicios, ILogger logger, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO inventario.productos (producto_id, nombre, precio, stock_disponible) VALUES
                ('P001', 'Teclado mecánico',  45000,  10),
                ('P002', 'Mouse inalámbrico', 18000,  20),
                ('P003', 'Monitor 24"',       210000,  5),
                ('P004', 'Auriculares',       32000,   8),
                ('P005', 'Notebook',          950000,  2)
            ON CONFLICT (producto_id) DO NOTHING
            """;

        var dataSource = servicios.GetRequiredService<NpgsqlDataSource>();

        // PostgreSQL puede tardar en aceptar conexiones: se reintenta antes de fallar
        for (var intento = 1; ; intento++)
        {
            try
            {
                await using var conexion = await dataSource.OpenConnectionAsync(ct);
                var insertados = await conexion.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
                logger.LogInformation("Datos iniciales de inventario verificados ({Insertados} productos nuevos).", insertados);
                return;
            }
            catch (NpgsqlException) when (intento < 10)
            {
                logger.LogWarning("PostgreSQL no responde (intento {Intento}/10). Reintentando en 3 segundos...", intento);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }
    }
}
