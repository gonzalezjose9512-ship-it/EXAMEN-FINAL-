namespace Pedidos.Domain.Eventos;

/// <summary>La saga terminó bien: hay stock reservado, pago aprobado y envío programado.</summary>
public sealed record PedidoConfirmado(string CodigoAutorizacion, string CodigoSeguimiento, DateOnly FechaEstimadaEntrega) : EventoPedido;
