namespace Pedidos.Domain.Eventos;

/// <summary>El cliente pidió confirmar: arrancó la saga y el pedido pasó a PendienteConfirmacion.</summary>
public sealed record ConfirmacionSolicitada(Guid SagaId) : EventoPedido;
