namespace Pedidos.Domain;

/// <summary>
/// Una línea del pedido: producto, cantidad y precio unitario informado por el cliente.
/// </summary>
public sealed record ItemPedido(string ProductoId, int Cantidad, decimal PrecioUnitario)
{
    /// <summary>Cantidad × precio unitario.</summary>
    public decimal Subtotal => Cantidad * PrecioUnitario;
}
