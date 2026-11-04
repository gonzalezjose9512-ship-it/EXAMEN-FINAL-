namespace Pedidos.Domain.Eventos;

/// <summary>
/// La saga falló y se compensó lo hecho. Motivo es el código del servicio que rechazó
/// (por ejemplo STOCK_INSUFICIENTE, TARJETA_RECHAZADA o SIN_CUPO).
/// </summary>
public sealed record PedidoRechazado(string Motivo) : EventoPedido;
