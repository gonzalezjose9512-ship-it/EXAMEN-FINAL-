namespace Pagos.Service.Abstracciones;

/// <summary>
/// Acceso a los datos del esquema pagos. Lo implementa PagosRepository con Npgsql.
/// Todas las operaciones se ejecutan dentro de la transacción abierta con IniciarTransaccionAsync.
/// Los handlers dependen de esta interfaz para que las pruebas usen un doble sin PostgreSQL.
/// </summary>
public interface IPagosRepository
{
    // ----- Transacción -----
    Task IniciarTransaccionAsync(CancellationToken ct = default);
    Task ConfirmarTransaccionAsync(CancellationToken ct = default);
    Task DeshacerTransaccionAsync(CancellationToken ct = default);

    // ----- Idempotencia (pagos.mensajes_procesados) -----
    Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default);
    Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default);

    // ----- Cobrar -----

    /// <summary>
    /// INSERT en pagos.pagos (Aprobado o Rechazado). Genera el pago_id con la secuencia (por ejemplo PG-000042).
    /// Nunca recibe ni guarda el número completo de la tarjeta.
    /// </summary>
    Task<string> RegistrarPagoAsync(NuevoPago pago, CancellationToken ct = default);

    // ----- Reembolsar -----

    /// <summary>SELECT estado, monto FROM pagos.pagos WHERE pago_id = @id FOR UPDATE. Null si no existe.</summary>
    Task<PagoBloqueado?> ObtenerPagoBloqueandoAsync(string pagoId, CancellationToken ct = default);

    /// <summary>UPDATE pagos.pagos SET estado = 'Reembolsado', fecha_reembolso = @fecha.</summary>
    Task MarcarReembolsadoAsync(string pagoId, DateTime fechaReembolsoUtc, CancellationToken ct = default);
}

/// <summary>Datos de un intento de cobro a registrar.</summary>
public sealed record NuevoPago(
    Guid PedidoId,
    Guid SagaId,
    decimal Monto,
    string Estado,
    string? Motivo,
    string TarjetaUltimos4,
    string Titular,
    string? CodigoAutorizacion,
    DateTime FechaUtc);

/// <summary>Estado y monto de un pago leído con bloqueo.</summary>
public sealed record PagoBloqueado(string Estado, decimal Monto);
