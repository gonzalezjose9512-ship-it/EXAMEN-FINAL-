namespace Pedidos.Application.Excepciones;

/// <summary>
/// El pedido o la saga no existe.
/// La lanzan los casos de uso cuando el event store o el repositorio de sagas no devuelve nada.
/// La API la traduce a 404 Not Found.
/// </summary>
public sealed class NoEncontradoException(string mensaje) : Exception(mensaje);
