using Npgsql;
using Pedidos.Application.Puertos;

namespace Pedidos.Infrastructure.Persistencia;

/// <summary>
/// Adaptador de IUnitOfWork con PostgreSQL.
///
/// Tiene UNA conexión y, cuando se pide, UNA NpgsqlTransaction que comparten
/// PostgresEventStore y PostgresSagaRepository. Así, al confirmar un pedido, el evento,
/// la saga y su primer paso se guardan juntos o no se guarda nada.
///
/// Se registra con vida "scoped": una instancia por request HTTP (o por mensaje procesado).
/// </summary>
public sealed class PostgresUnitOfWork(NpgsqlDataSource dataSource) : IUnitOfWork, IAsyncDisposable
{
    private NpgsqlConnection? _conexion;

    /// <summary>Transacción abierta, o null si no hay ninguna (lecturas y escrituras sueltas).</summary>
    public NpgsqlTransaction? Transaccion { get; private set; }

    /// <summary>Devuelve la conexión del scope; la abre la primera vez.</summary>
    public async Task<NpgsqlConnection> ObtenerConexionAsync(CancellationToken ct = default)
    {
        _conexion ??= await dataSource.OpenConnectionAsync(ct);
        return _conexion;
    }

    public async Task IniciarAsync(CancellationToken ct = default)
    {
        if (Transaccion is not null)
            throw new InvalidOperationException("Ya hay una transacción abierta.");

        var conexion = await ObtenerConexionAsync(ct);
        Transaccion = await conexion.BeginTransactionAsync(ct);
    }

    public async Task ConfirmarAsync(CancellationToken ct = default)
    {
        if (Transaccion is null)
            throw new InvalidOperationException("No hay una transacción abierta para confirmar.");

        await Transaccion.CommitAsync(ct);
        await Transaccion.DisposeAsync();
        Transaccion = null;
    }

    public async Task DeshacerAsync(CancellationToken ct = default)
    {
        if (Transaccion is null)
            return;

        await Transaccion.RollbackAsync(ct);
        await Transaccion.DisposeAsync();
        Transaccion = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Transaccion is not null)
            await Transaccion.DisposeAsync();
        if (_conexion is not null)
            await _conexion.DisposeAsync();
    }
}
