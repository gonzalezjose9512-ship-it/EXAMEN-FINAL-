using Microsoft.AspNetCore.Mvc;
using Pedidos.Api.Modelos;
using Pedidos.Domain;
using TiendaNorte.Contracts;
using CasosDeUso = Pedidos.Application.CasosDeUso;

namespace Pedidos.Api.Controllers;

/// <summary>
/// Adaptador de entrada HTTP de Pedidos. Cada acción: recibe el request, valida el formato
/// (atributos + [ApiController]), llama al caso de uso y devuelve la respuesta.
/// SIN lógica de negocio y sin try/catch: los errores los traduce ManejadorDeErrores.
/// </summary>
[ApiController]
[Route("api/pedidos")]
[Produces("application/json")]
public sealed class PedidosController(
    CasosDeUso.CrearPedido crearPedido,
    CasosDeUso.AgregarItem agregarItem,
    CasosDeUso.ObtenerPedido obtenerPedido,
    CasosDeUso.ObtenerEventos obtenerEventos,
    CasosDeUso.ConfirmarPedido confirmarPedido) : ControllerBase
{
    /// <summary>1. Crear pedido: lo deja en Borrador y guarda PedidoCreado.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CrearPedidoResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Crear([FromBody] CrearPedidoRequest request, CancellationToken ct)
    {
        var d = request.DireccionEnvio!;
        var direccion = new Direccion(d.Calle!, d.Numero!, d.Ciudad!, d.CodigoPostal!);

        var pedido = await crearPedido.EjecutarAsync(request.ClienteId!, direccion, ct);

        var respuesta = new CrearPedidoResponse(pedido.Id, pedido.Estado.ToString(), pedido.Version);
        return CreatedAtAction(nameof(Obtener), new { pedidoId = pedido.Id }, respuesta);
    }

    /// <summary>2. Agregar ítem: solo en Borrador; guarda ItemAgregado.</summary>
    [HttpPost("{pedidoId:guid}/items")]
    [ProducesResponseType(typeof(PedidoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AgregarItem(Guid pedidoId, [FromBody] AgregarItemRequest request, CancellationToken ct)
    {
        var pedido = await agregarItem.EjecutarAsync(pedidoId, request.ProductoId!, request.Cantidad, request.PrecioUnitario, ct);
        return Ok(pedido.ToResponse());
    }

    /// <summary>
    /// 3. Confirmar pedido: inicia la saga y responde 202 sin esperar el resultado.
    /// El avance se consulta en GET /api/sagas/{sagaId}.
    /// </summary>
    [HttpPost("{pedidoId:guid}/confirmar")]
    [ProducesResponseType(typeof(ConfirmarPedidoResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Confirmar(Guid pedidoId, [FromBody] ConfirmarPedidoRequest request, CancellationToken ct)
    {
        var t = request.Tarjeta!;
        var tarjeta = new DatosTarjeta(t.Numero!, t.Titular!, t.Vencimiento!);

        var resultado = await confirmarPedido.EjecutarAsync(pedidoId, tarjeta, request.ObtenerFechaEnvioPreferida(), ct);

        var respuesta = new ConfirmarPedidoResponse(resultado.PedidoId, resultado.SagaId, resultado.Estado);
        return AcceptedAtAction(nameof(SagasController.Obtener), "Sagas", new { sagaId = resultado.SagaId }, respuesta);
    }

    /// <summary>4. Consultar pedido: reconstruido desde sus eventos.</summary>
    [HttpGet("{pedidoId:guid}")]
    [ProducesResponseType(typeof(PedidoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(Guid pedidoId, CancellationToken ct)
    {
        var pedido = await obtenerPedido.EjecutarAsync(pedidoId, ct);
        return Ok(pedido.ToResponse());
    }

    /// <summary>5. Consultar eventos del pedido, ordenados por versión.</summary>
    [HttpGet("{pedidoId:guid}/eventos")]
    [ProducesResponseType(typeof(IReadOnlyList<EventoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObtenerEventos(Guid pedidoId, CancellationToken ct)
    {
        var eventos = await obtenerEventos.EjecutarAsync(pedidoId, ct);
        return Ok(eventos.Select(e => e.ToResponse()).ToList());
    }
}
