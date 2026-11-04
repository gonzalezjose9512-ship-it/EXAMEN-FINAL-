namespace TiendaNorte.Contracts;

// ---------------------------------------------------------------------------
// COMANDOS: los publica Pedidos (la saga) en la cola de cada servicio.
// Cada record es el contenido del campo "datos" del sobre Mensaje<T>.
// ---------------------------------------------------------------------------

/// <summary>Un producto y su cantidad. Se usa en ReservarStock y en StockReservado.</summary>
public sealed record ItemStock(string ProductoId, int Cantidad);

/// <summary>Datos de la tarjeta que viajan en el comando Cobrar.</summary>
public sealed record DatosTarjeta(string Numero, string Titular, string Vencimiento);

/// <summary>Dirección de envío. Los campos pueden venir vacíos: Logística responde DIRECCION_INVALIDA.</summary>
public sealed record DireccionEnvio(string? Calle, string? Numero, string? Ciudad, string? CodigoPostal);

/// <summary>Cola inventario.comandos. Reserva el stock de todos los ítems del pedido.</summary>
public sealed record ReservarStock(IReadOnlyList<ItemStock> Items);

/// <summary>Cola inventario.comandos. Compensación: devuelve al stock lo reservado.</summary>
public sealed record LiberarStock(string ReservaId);

/// <summary>Cola pagos.comandos. Cobra el total del pedido (con el descuento ya aplicado).</summary>
public sealed record Cobrar(decimal Monto, DatosTarjeta Tarjeta);

/// <summary>Cola pagos.comandos. Compensación: reembolsa un pago aprobado.</summary>
public sealed record Reembolsar(string PagoId);

/// <summary>Cola logistica.comandos. Programa el envío. FechaPreferida es opcional.</summary>
public sealed record ProgramarEnvio(DireccionEnvio Direccion, DateOnly? FechaPreferida);

/// <summary>Cola logistica.comandos. Compensación: anula un envío programado.</summary>
public sealed record AnularEnvio(string EnvioId);
