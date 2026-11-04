namespace TiendaNorte.Contracts;

// ---------------------------------------------------------------------------
// EVENTOS: los publican los 3 servicios en la cola pedidos.respuestas.
// Cada record es el contenido del campo "datos" del sobre Mensaje<T>.
// Los campos opcionales (null) no se escriben en el JSON.
// ---------------------------------------------------------------------------

/// <summary>Inventario: todos los ítems tenían stock y se creó la reserva.</summary>
public sealed record StockReservado(string ReservaId, IReadOnlyList<ItemStock> Items);

/// <summary>
/// Inventario: falta stock (STOCK_INSUFICIENTE, con Solicitado y Disponible)
/// o el producto no existe (PRODUCTO_INEXISTENTE).
/// </summary>
public sealed record StockRechazado(string Motivo, string ProductoId, int? Solicitado = null, int? Disponible = null);

/// <summary>Inventario: compensación terminada. Se publica SIEMPRE, aunque no hubiera nada que liberar.</summary>
public sealed record StockLiberado(string ReservaId);

/// <summary>Pagos: el cobro pasó todas las validaciones.</summary>
public sealed record PagoAprobado(string PagoId, decimal Monto, string CodigoAutorizacion);

/// <summary>
/// Pagos: el cobro fue rechazado (DATOS_INVALIDOS con Detalle, TARJETA_RECHAZADA,
/// o MONTO_EXCEDIDO con Limite). El rechazo también tiene PagoId.
/// </summary>
public sealed record PagoRechazado(string PagoId, string Motivo, string? Detalle = null, decimal? Limite = null);

/// <summary>Pagos: compensación terminada. Monto = 0 si el pago estaba Rechazado o no existía.</summary>
public sealed record PagoReembolsado(string PagoId, decimal Monto);

/// <summary>Logística: había cupo para la fecha y se programó el envío.</summary>
public sealed record EnvioProgramado(string EnvioId, string CodigoSeguimiento, DateOnly FechaDespacho, DateOnly FechaEstimadaEntrega);

/// <summary>
/// Logística: SIN_CUPO (con Fecha y CupoDiario), DIRECCION_INVALIDA (con Detalle)
/// o FECHA_INVALIDA (con Fecha).
/// </summary>
public sealed record EnvioRechazado(string Motivo, DateOnly? Fecha = null, int? CupoDiario = null, string? Detalle = null);

/// <summary>Logística: compensación terminada. Se publica SIEMPRE, aunque no hubiera nada que anular.</summary>
public sealed record EnvioAnulado(string EnvioId);
