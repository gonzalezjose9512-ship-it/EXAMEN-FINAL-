namespace Pedidos.Domain;

/// <summary>
/// Dirección de envío del pedido (value object: dos direcciones con los mismos datos son iguales).
/// </summary>
public sealed record Direccion(string Calle, string Numero, string Ciudad, string CodigoPostal);
