namespace Pedidos.Domain.Eventos;

/// <summary>Se creó un pedido vacío en estado Borrador. Siempre es la versión 1.</summary>
public sealed record PedidoCreado(string ClienteId, Direccion DireccionEnvio) : EventoPedido;
