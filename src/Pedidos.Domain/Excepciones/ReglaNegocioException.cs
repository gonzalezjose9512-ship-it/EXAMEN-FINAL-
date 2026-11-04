namespace Pedidos.Domain.Excepciones;

/// <summary>
/// La operación viola una regla de negocio del pedido.
/// Ejemplo: cantidad fuera de 1 a 10, más de 10 unidades del mismo producto,
/// o confirmar un pedido sin ítems.
/// La API la traduce a 422 Unprocessable Entity.
/// </summary>
public sealed class ReglaNegocioException(string mensaje) : Exception(mensaje);
