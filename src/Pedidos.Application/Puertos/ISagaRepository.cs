using Pedidos.Application.Sagas;

namespace Pedidos.Application.Puertos;

/// <summary>
/// Puerto de salida: persistencia de las sagas (tablas pedidos.sagas y pedidos.saga_pasos)
/// y de los mensajes ya procesados (pedidos.mensajes_procesados).
/// Lo implementa PostgresSagaRepository en Pedidos.Infrastructure.
/// </summary>
public interface ISagaRepository
{
    /// <summary>Carga la saga con sus pasos ordenados por id. Null si no existe.</summary>
    Task<SagaConfirmacion?> ObtenerAsync(Guid sagaId, CancellationToken ct = default);

    /// <summary>Inserta la saga nueva y sus pasos.</summary>
    Task CrearAsync(SagaConfirmacion saga, CancellationToken ct = default);

    /// <summary>
    /// Guarda el estado actual de la saga: UPDATE de pedidos.sagas, UPDATE de los pasos que ya existían
    /// e INSERT de los pasos nuevos (los que todavía no tienen Id).
    /// </summary>
    Task ActualizarAsync(SagaConfirmacion saga, CancellationToken ct = default);

    /// <summary>Idempotencia: true si el messageId ya está en pedidos.mensajes_procesados.</summary>
    Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>Idempotencia: inserta el messageId en pedidos.mensajes_procesados.</summary>
    Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default);
}
