namespace Pedidos.Domain;

/// <summary>
/// Estados del pedido: Borrador → PendienteConfirmacion → Confirmado (si la saga termina bien)
/// o Rechazado (si falla algún paso y se compensa).
/// </summary>
public enum EstadoPedido
{
    Borrador,
    PendienteConfirmacion,
    Confirmado,
    Rechazado
}
