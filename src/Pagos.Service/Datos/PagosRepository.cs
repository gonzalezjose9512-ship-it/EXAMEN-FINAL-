using Dapper;
using Npgsql;
using Pagos.Service.Abstracciones;

namespace Pagos.Service.Datos;

/// <summary>
/// Implementa IPagosRepository con Npgsql + Dapper sobre el esquema pagos.
/// Una conexión y una transacción por mensaje procesado (vida scoped).
/// </summary>
public sealed class PagosRepository(NpgsqlDataSource dataSource) : IPagosRepository, IAsyncDisposable
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
            "SELECT EXISTS (SELECT 1 FROM pagos.mensajes_procesados WHERE message_id = @messageId)",
            new { messageId }, _transaccion, cancellationToken: ct));

    public Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Conexion.ExecuteAsync(new CommandDefinition(
            "INSERT INTO pagos.mensajes_procesados (message_id, fecha_utc) VALUES (@messageId, now())",
            new { messageId }, _transaccion, cancellationToken: ct));

    public async Task<string> RegistrarPagoAsync(NuevoPago pago, CancellationToken ct = default)
    {
        // Id con prefijo desde la secuencia: PG-000001
        var pagoId = await Conexion.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT 'PG-' || lpad(nextval('pagos.pagos_seq')::text, 6, '0')",
            transaction: _transaccion, cancellationToken: ct));

        await Conexion.ExecuteAsync(new CommandDefinition("""
            INSERT INTO pagos.pagos
                (pago_id, pedido_id, saga_id, monto, estado, motivo, tarjeta_ultimos4, titular, codigo_autorizacion, fecha_utc)
            VALUES
                (@PagoId, @PedidoId, @SagaId, @Monto, @Estado, @Motivo, @TarjetaUltimos4, @Titular, @CodigoAutorizacion, @FechaUtc)
            """,
            new
            {
                PagoId = pagoId,
                pago.PedidoId,
                pago.SagaId,
                pago.Monto,
                pago.Estado,
                pago.Motivo,
                pago.TarjetaUltimos4,
                pago.Titular,
                pago.CodigoAutorizacion,
                pago.FechaUtc
            },
            _transaccion, cancellationToken: ct));

        return pagoId!;
    }

    public async Task<PagoBloqueado?> ObtenerPagoBloqueandoAsync(string pagoId, CancellationToken ct = default)
    {
        var fila = await Conexion.QuerySingleOrDefaultAsync<FilaPago>(new CommandDefinition(
            "SELECT estado AS Estado, monto AS Monto FROM pagos.pagos WHERE pago_id = @pagoId FOR UPDATE",
            new { pagoId }, _transaccion, cancellationToken: ct));

        return fila is null ? null : new PagoBloqueado(fila.Estado, fila.Monto);
    }

    public Task MarcarReembolsadoAsync(string pagoId, DateTime fechaReembolsoUtc, CancellationToken ct = default) =>
        Conexion.ExecuteAsync(new CommandDefinition(
            "UPDATE pagos.pagos SET estado = 'Reembolsado', fecha_reembolso = @fechaReembolsoUtc WHERE pago_id = @pagoId",
            new { pagoId, fechaReembolsoUtc }, _transaccion, cancellationToken: ct));

    private sealed class FilaPago
    {
        public string Estado { get; set; } = string.Empty;
        public decimal Monto { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaccion is not null)
            await _transaccion.DisposeAsync();
        if (_conexion is not null)
            await _conexion.DisposeAsync();
    }
}
