using TiendaNorte.Contracts;

namespace Logistica.Service.Abstracciones;

/// <summary>
/// Acceso a los datos del esquema logistica. Lo implementa EnviosRepository con Npgsql.
/// Todas las operaciones se ejecutan dentro de la transacción abierta con IniciarTransaccionAsync.
/// Los handlers dependen de esta interfaz para que las pruebas usen un doble sin PostgreSQL.
/// </summary>
public interface IEnviosRepository
{
    // ----- Transacción -----
    Task IniciarTransaccionAsync(CancellationToken ct = default);
    Task ConfirmarTransaccionAsync(CancellationToken ct = default);
    Task DeshacerTransaccionAsync(CancellationToken ct = default);

    // ----- Idempotencia (logistica.mensajes_procesados) -----
    Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default);
    Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default);

    // ----- ProgramarEnvio -----

    /// <summary>SELECT COUNT(*) FROM logistica.envios WHERE fecha_despacho = @fecha AND estado = 'Programado'.</summary>
    Task<int> ContarProgramadosAsync(DateOnly fechaDespacho, CancellationToken ct = default);

    /// <summary>
    /// INSERT del envío con estado 'Programado'. Genera envio_id y codigo_seguimiento con la misma
    /// secuencia (por ejemplo E-000031 y TN-000031).
    /// </summary>
    Task<EnvioCreado> CrearEnvioAsync(NuevoEnvio envio, CancellationToken ct = default);

    // ----- AnularEnvio -----

    /// <summary>SELECT estado FROM logistica.envios WHERE envio_id = @id FOR UPDATE. Null si no existe.</summary>
    Task<string?> ObtenerEstadoEnvioBloqueandoAsync(string envioId, CancellationToken ct = default);

    /// <summary>UPDATE logistica.envios SET estado = 'Anulado'. Libera el cupo de esa fecha.</summary>
    Task AnularEnvioAsync(string envioId, CancellationToken ct = default);
}

/// <summary>Datos de un envío a programar.</summary>
public sealed record NuevoEnvio(
    Guid PedidoId,
    Guid SagaId,
    DireccionEnvio Direccion,
    DateOnly FechaDespacho,
    DateOnly FechaEstimadaEntrega,
    DateTime FechaUtc);

/// <summary>Ids generados al crear el envío.</summary>
public sealed record EnvioCreado(string EnvioId, string CodigoSeguimiento);
