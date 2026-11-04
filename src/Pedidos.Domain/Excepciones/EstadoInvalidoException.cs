namespace Pedidos.Domain.Excepciones;

/// <summary>
/// La operación no está permitida en el estado actual del pedido.
/// Ejemplo: agregar un ítem a un pedido que no está en Borrador, o confirmarlo dos veces.
/// La API la traduce a 409 Conflict.
/// </summary>
public sealed class EstadoInvalidoException(string mensaje) : Exception(mensaje);
