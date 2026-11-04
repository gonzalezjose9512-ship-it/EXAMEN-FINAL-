using Pedidos.Domain.Eventos;
using Pedidos.Domain.Excepciones;

namespace Pedidos.Domain;

/// <summary>
/// Agregado Pedido con Event Sourcing (Hito 2).
///
/// El pedido NO se guarda como una fila con su estado: se guardan sus eventos y el estado
/// se reconstruye aplicándolos en orden con <see cref="Rehidratar"/>.
///
/// Flujo de cada comando (Crear, AgregarItem, SolicitarConfirmacion, Confirmar, Rechazar):
///   1. valida las reglas de negocio (lanza excepción si no se cumplen);
///   2. genera el evento correspondiente con la versión siguiente;
///   3. lo aplica con su método Apply (así cambia el estado);
///   4. lo deja en <see cref="EventosNuevos"/> para que el caso de uso lo guarde.
/// </summary>
public sealed class Pedido
{
    public const int CantidadMinimaPorItem = 1;
    public const int CantidadMaximaPorProducto = 10;
    public const decimal MontoMinimoParaDescuento = 100_000m;
    public const decimal PorcentajeDescuento = 0.10m;

    private readonly List<ItemPedido> _items = [];
    private readonly List<EventoPedido> _eventosNuevos = [];

    // ===================== Estado (solo se modifica dentro de los Apply) =====================

    public Guid Id { get; private set; }
    public string ClienteId { get; private set; } = string.Empty;
    public Direccion DireccionEnvio { get; private set; } = null!;
    public EstadoPedido Estado { get; private set; }
    public IReadOnlyList<ItemPedido> Items => _items;

    /// <summary>Versión del último evento aplicado. 0 = pedido sin eventos.</summary>
    public int Version { get; private set; }

    public Guid? SagaId { get; private set; }
    public string? CodigoAutorizacion { get; private set; }
    public string? CodigoSeguimiento { get; private set; }
    public DateOnly? FechaEstimadaEntrega { get; private set; }
    public string? MotivoRechazo { get; private set; }

    /// <summary>Suma de los subtotales de los ítems.</summary>
    public decimal Subtotal => _items.Sum(i => i.Subtotal);

    /// <summary>10% del subtotal si SUPERA $100.000 (con $100.000 exactos no hay descuento).</summary>
    public decimal Descuento => Subtotal > MontoMinimoParaDescuento
        ? Math.Round(Subtotal * PorcentajeDescuento, 2)
        : 0m;

    /// <summary>Subtotal menos descuento. Es el monto que se cobra.</summary>
    public decimal Total => Subtotal - Descuento;

    /// <summary>Eventos generados por los comandos y todavía no guardados en el event store.</summary>
    public IReadOnlyList<EventoPedido> EventosNuevos => _eventosNuevos;

    private Pedido() { }

    // ===================== Comandos (validan y generan eventos) =====================

    /// <summary>Crea un pedido vacío en estado Borrador y genera PedidoCreado (versión 1).</summary>
    public static Pedido Crear(Guid pedidoId, string clienteId, Direccion direccion)
    {
        if (pedidoId == Guid.Empty)
            throw new ReglaNegocioException("El pedido necesita un id.");
        if (string.IsNullOrWhiteSpace(clienteId))
            throw new ReglaNegocioException("El pedido necesita un cliente.");
        ArgumentNullException.ThrowIfNull(direccion);

        var pedido = new Pedido { Id = pedidoId };
        pedido.Registrar(new PedidoCreado(clienteId, direccion));
        return pedido;
    }

    /// <summary>
    /// Agrega un producto. Solo en Borrador (si no, 409). La cantidad va de 1 a 10 y la suma
    /// de cantidades del mismo producto no puede superar 10 (si no, 422). El dominio valida
    /// la cantidad aunque el controller ya lo haya hecho: no confía en las capas de afuera.
    /// </summary>
    public void AgregarItem(string productoId, int cantidad, decimal precioUnitario)
    {
        if (Estado != EstadoPedido.Borrador)
            throw new EstadoInvalidoException(
                $"Solo se pueden agregar ítems a un pedido en estado Borrador. Estado actual: {Estado}.");

        if (string.IsNullOrWhiteSpace(productoId))
            throw new ReglaNegocioException("El ítem necesita un productoId.");

        if (cantidad < CantidadMinimaPorItem || cantidad > CantidadMaximaPorProducto)
            throw new ReglaNegocioException(
                $"La cantidad de cada ítem debe estar entre {CantidadMinimaPorItem} y {CantidadMaximaPorProducto}. Cantidad recibida: {cantidad}.");

        if (precioUnitario <= 0)
            throw new ReglaNegocioException("El precio unitario debe ser mayor a 0.");

        var cantidadActual = _items.Where(i => i.ProductoId == productoId).Sum(i => i.Cantidad);
        if (cantidadActual + cantidad > CantidadMaximaPorProducto)
            throw new ReglaNegocioException(
                $"La suma de cantidades del producto {productoId} no puede superar {CantidadMaximaPorProducto}. Ya hay {cantidadActual} y se quieren agregar {cantidad}.");

        Registrar(new ItemAgregado(productoId, cantidad, precioUnitario));
    }

    /// <summary>
    /// Inicia la confirmación. Solo en Borrador (409: evita confirmar dos veces y reintentar
    /// un pedido Rechazado). Necesita al menos un ítem (422).
    /// </summary>
    public void SolicitarConfirmacion(Guid sagaId)
    {
        if (Estado != EstadoPedido.Borrador)
            throw new EstadoInvalidoException(
                $"Solo se puede confirmar un pedido en estado Borrador. Estado actual: {Estado}.");

        if (_items.Count == 0)
            throw new ReglaNegocioException("No se puede confirmar un pedido sin ítems.");

        Registrar(new ConfirmacionSolicitada(sagaId));
    }

    /// <summary>La saga terminó bien. Solo en PendienteConfirmacion (si no, 409).</summary>
    public void Confirmar(string codigoAutorizacion, string codigoSeguimiento, DateOnly fechaEstimadaEntrega)
    {
        ValidarPendienteConfirmacion("confirmar");
        Registrar(new PedidoConfirmado(codigoAutorizacion, codigoSeguimiento, fechaEstimadaEntrega));
    }

    /// <summary>La saga falló y se compensó. Solo en PendienteConfirmacion (si no, 409).</summary>
    public void Rechazar(string motivo)
    {
        ValidarPendienteConfirmacion("rechazar");
        Registrar(new PedidoRechazado(motivo));
    }

    private void ValidarPendienteConfirmacion(string operacion)
    {
        if (Estado != EstadoPedido.PendienteConfirmacion)
            throw new EstadoInvalidoException(
                $"Solo se puede {operacion} un pedido en estado PendienteConfirmacion. Estado actual: {Estado}.");
    }

    // ===================== Event Sourcing =====================

    /// <summary>
    /// Reconstruye el pedido aplicando sus eventos en orden de versión.
    /// No genera eventos nuevos: solo recupera el estado.
    /// </summary>
    public static Pedido Rehidratar(IEnumerable<EventoPedido> eventos)
    {
        ArgumentNullException.ThrowIfNull(eventos);

        var pedido = new Pedido();
        foreach (var evento in eventos.OrderBy(e => e.Version))
            pedido.Aplicar(evento);

        if (pedido.Version == 0)
            throw new InvalidOperationException("No se puede rehidratar un pedido sin eventos.");

        return pedido;
    }

    /// <summary>Completa los metadatos del evento nuevo, lo aplica y lo deja para guardar.</summary>
    private void Registrar(EventoPedido evento)
    {
        var eventoCompleto = evento with
        {
            PedidoId = Id,
            Version = Version + 1,
            FechaUtc = DateTime.UtcNow
        };

        Aplicar(eventoCompleto);
        _eventosNuevos.Add(eventoCompleto);
    }

    /// <summary>Llama al Apply que corresponde al tipo de evento y actualiza la versión.</summary>
    private void Aplicar(EventoPedido evento)
    {
        switch (evento)
        {
            case PedidoCreado e: Apply(e); break;
            case ItemAgregado e: Apply(e); break;
            case ConfirmacionSolicitada e: Apply(e); break;
            case PedidoConfirmado e: Apply(e); break;
            case PedidoRechazado e: Apply(e); break;
            default:
                throw new InvalidOperationException($"Evento desconocido: {evento.GetType().Name}.");
        }

        Version = evento.Version;
    }

    // Un Apply por tipo de evento. Solo cambian el estado: NO validan (el evento ya ocurrió).

    private void Apply(PedidoCreado e)
    {
        Id = e.PedidoId;
        ClienteId = e.ClienteId;
        DireccionEnvio = e.DireccionEnvio;
        Estado = EstadoPedido.Borrador;
    }

    private void Apply(ItemAgregado e)
    {
        _items.Add(new ItemPedido(e.ProductoId, e.Cantidad, e.PrecioUnitario));
    }

    private void Apply(ConfirmacionSolicitada e)
    {
        SagaId = e.SagaId;
        Estado = EstadoPedido.PendienteConfirmacion;
    }

    private void Apply(PedidoConfirmado e)
    {
        CodigoAutorizacion = e.CodigoAutorizacion;
        CodigoSeguimiento = e.CodigoSeguimiento;
        FechaEstimadaEntrega = e.FechaEstimadaEntrega;
        Estado = EstadoPedido.Confirmado;
    }

    private void Apply(PedidoRechazado e)
    {
        MotivoRechazo = e.Motivo;
        Estado = EstadoPedido.Rechazado;
    }
}
