using Microsoft.AspNetCore.Mvc;
using Pedidos.Api.Modelos;
using CasosDeUso = Pedidos.Application.CasosDeUso;

namespace Pedidos.Api.Controllers;

/// <summary>
/// Adaptador de entrada HTTP para seguir el avance de la saga después del 202 de confirmar.
/// Sin lógica de negocio.
/// </summary>
[ApiController]
[Route("api/sagas")]
[Produces("application/json")]
public sealed class SagasController(CasosDeUso.ObtenerSaga obtenerSaga) : ControllerBase
{
    /// <summary>6. Consultar saga: estado e historial de pasos y compensaciones.</summary>
    [HttpGet("{sagaId:guid}")]
    [ProducesResponseType(typeof(SagaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(Guid sagaId, CancellationToken ct)
    {
        var saga = await obtenerSaga.EjecutarAsync(sagaId, ct);
        return Ok(saga.ToResponse());
    }
}
