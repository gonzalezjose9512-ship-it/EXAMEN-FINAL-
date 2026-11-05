using Pedidos.Application.Excepciones;
using Pedidos.Application.Puertos;
using Pedidos.Application.Sagas;

namespace Pedidos.Application.CasosDeUso;

/// <summary>
/// Endpoint 6 - GET /api/sagas/{sagaId}.
/// Devuelve la saga con el historial de sus pasos y compensaciones. No escribe ni publica.
/// </summary>
public sealed class ObtenerSaga(ISagaRepository sagaRepository)
{
    public async Task<SagaConfirmacion> EjecutarAsync(Guid sagaId, CancellationToken ct = default) =>
        await sagaRepository.ObtenerAsync(sagaId, ct)
        ?? throw new NoEncontradoException($"No existe la saga {sagaId}.");
}
