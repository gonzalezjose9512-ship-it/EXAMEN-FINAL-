using Dapper;
using Logistica.Service.Abstracciones;
using Npgsql;

namespace Logistica.Service.Datos;

/// <summary>
/// Implementa IEnviosRepository con Npgsql + Dapper sobre el esquema logistica.
/// Una conexión y una transacción por mensaje procesado (vida scoped).
/// Las fechas (DateOnly) se envían como DateTime y se convierten a date en el SQL.
/// </summary>
public sealed class EnviosRepository(NpgsqlDataSource dataSource) : IEnviosRepository, IAsyncDisposable
{
    private NpgsqlConnection? _conexion;
    private NpgsqlTransaction? _transaccion;

    private NpgsqlConnection Conexion =>
        _conexion ?? throw new InvalidOperationException("Llamá primero a IniciarTransaccionAsync.");

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

    public Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Conexion.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM logistica.mensajes_procesados WHERE message_id = @messageId)",
            new { messageId }, _transaccion, cancellationToken: ct));

    public Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Conexion.ExecuteAsync(new CommandDefinition(
            "INSERT INTO logistica.mensajes_procesados (message_id, fecha_utc) VALUES (@messageId, now())",
            new { messageId }, _transaccion, cancellationToken: ct));

    public Task<int> ContarProgramadosAsync(DateOnly fechaDespacho, CancellationToken ct = default) =>
        Conexion.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*)::int
            FROM logistica.envios
            WHERE fecha_despacho = CAST(@fecha AS date) AND estado = 'Programado'
            """, new { fecha = AFechaHora(fechaDespacho) }, _transaccion, cancellationToken: ct));

    public async Task<EnvioCreado> CrearEnvioAsync(NuevoEnvio envio, CancellationToken ct = default)
    {
        // Un solo número de la secuencia para los dos ids: E-000031 y TN-000031
        var numero = await Conexion.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT nextval('logistica.envios_seq')", transaction: _transaccion, cancellationToken: ct));
        var envioId = $"E-{numero:D6}";
        var codigoSeguimiento = $"TN-{numero:D6}";

        await Conexion.ExecuteAsync(new CommandDefinition("""
            INSERT INTO logistica.envios
                (envio_id, pedido_id, saga_id, estado, calle, numero, ciudad, codigo_postal,
                 fecha_despacho, fecha_estimada_entrega, codigo_seguimiento, fecha_utc)
            VALUES
                (@EnvioId, @PedidoId, @SagaId, 'Programado', @Calle, @Numero, @Ciudad, @CodigoPostal,
                 CAST(@FechaDespacho AS date), CAST(@FechaEntrega AS date), @CodigoSeguimiento, @FechaUtc)
            """,
            new
            {
                EnvioId = envioId,
                envio.PedidoId,
                envio.SagaId,
                envio.Direccion.Calle,
                envio.Direccion.Numero,
                envio.Direccion.Ciudad,
                envio.Direccion.CodigoPostal,
                FechaDespacho = AFechaHora(envio.FechaDespacho),
                FechaEntrega = AFechaHora(envio.FechaEstimadaEntrega),
                CodigoSeguimiento = codigoSeguimiento,
                envio.FechaUtc
            },
            _transaccion, cancellationToken: ct));

        return new EnvioCreado(envioId, codigoSeguimiento);
    }

    public Task<string?> ObtenerEstadoEnvioBloqueandoAsync(string envioId, CancellationToken ct = default) =>
        Conexion.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT estado FROM logistica.envios WHERE envio_id = @envioId FOR UPDATE",
            new { envioId }, _transaccion, cancellationToken: ct));

    public Task AnularEnvioAsync(string envioId, CancellationToken ct = default) =>
        Conexion.ExecuteAsync(new CommandDefinition(
            "UPDATE logistica.envios SET estado = 'Anulado' WHERE envio_id = @envioId",
            new { envioId }, _transaccion, cancellationToken: ct));

    private static DateTime AFechaHora(DateOnly fecha) => fecha.ToDateTime(TimeOnly.MinValue);

    public async ValueTask DisposeAsync()
    {
        if (_transaccion is not null)
            await _transaccion.DisposeAsync();
        if (_conexion is not null)
            await _conexion.DisposeAsync();
    }
}
