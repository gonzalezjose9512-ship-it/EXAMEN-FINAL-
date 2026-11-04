namespace Pedidos.Domain.Eventos;

/// <summary>Se agregó un producto al pedido (solo en estado Borrador).</summary>
public sealed record ItemAgregado(string ProductoId, int Cantidad, decimal PrecioUnitario) : EventoPedido;
