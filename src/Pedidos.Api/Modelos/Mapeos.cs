using System.Text.Json;
using Pedidos.Application.Puertos;
using Pedidos.Application.Sagas;
using Pedidos.Domain;

namespace Pedidos.Api.Modelos;

/// <summary>
/// Mapeo manual de objetos de Domain/Application a las respuestas HTTP.
/// Separa el modelo interno de lo que ve el cliente: si cambia uno, el otro no se rompe.
/// </summary>
public static class Mapeos
{
    public static PedidoResponse ToResponse(this Pedido pedido) => new(
        pedido.Id,
        pedido.ClienteId,
        pedido.Estado.ToString(),
        new DireccionResponse(
            pedido.DireccionEnvio.Calle,
            pedido.DireccionEnvio.Numero,
            pedido.DireccionEnvio.Ciudad,
            pedido.DireccionEnvio.CodigoPostal),
        pedido.Items
            .Select(i => new ItemResponse(i.ProductoId, i.Cantidad, i.PrecioUnitario, i.Subtotal))
            .ToList(),
        pedido.Subtotal,
        pedido.Descuento,
        pedido.Total,
        pedido.Version,
        pedido.Estado == EstadoPedido.Confirmado
            ? new ConfirmacionResponse(pedido.CodigoAutorizacion!, pedido.CodigoSeguimiento!, pedido.FechaEstimadaEntrega!.Value)
            : null,
        pedido.Estado == EstadoPedido.Rechazado ? pedido.MotivoRechazo : null);

    public static EventoResponse ToResponse(this EventoGuardado evento)
    {
        using var documento = JsonDocument.Parse(evento.DatosJson);
        return new EventoResponse(evento.Version, evento.Tipo, evento.FechaUtc, documento.RootElement.Clone());
    }

    public static SagaResponse ToResponse(this SagaConfirmacion saga) => new(
        saga.SagaId,
        saga.PedidoId,
        saga.Estado,
        saga.Pasos
            .Select(p => new PasoSagaResponse(p.Nombre, p.Estado, p.FechaUtc, p.Motivo, p.Resultado))
            .ToList(),
        saga.Error);
}
