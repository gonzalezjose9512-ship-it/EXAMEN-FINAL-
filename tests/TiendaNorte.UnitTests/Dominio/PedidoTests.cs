using Pedidos.Domain;
using Pedidos.Domain.Eventos;
using Pedidos.Domain.Excepciones;

namespace TiendaNorte.UnitTests.Dominio;

/// <summary>
/// Grupo A — Agregado Pedido y event sourcing (mínimo 6 pruebas).
/// Nombres con el patrón Metodo_Escenario_ResultadoEsperado y estructura Arrange / Act / Assert.
/// </summary>
public class PedidoTests
{
    private static readonly Direccion DireccionDePrueba =
        new("Av. Mate de Luna", "1234", "San Miguel de Tucumán", "4000");

    private static Pedido CrearPedidoEnBorrador() =>
        Pedido.Crear(Guid.NewGuid(), "CLI-001", DireccionDePrueba);

    // ---------------------------------------------------------------------
    // A1. Crear un pedido genera el evento PedidoCreado con los datos correctos.
    // ---------------------------------------------------------------------
    [Fact]
    public void Crear_ConDatosValidos_GeneraEventoPedidoCreado()
    {
        // Arrange
        var pedidoId = Guid.NewGuid();

        // Act
        var pedido = Pedido.Crear(pedidoId, "CLI-001", DireccionDePrueba);

        // Assert
        var evento = Assert.IsType<PedidoCreado>(Assert.Single(pedido.EventosNuevos));
        Assert.Equal(pedidoId, evento.PedidoId);
        Assert.Equal(1, evento.Version);
        Assert.Equal("CLI-001", evento.ClienteId);
        Assert.Equal(DireccionDePrueba, evento.DireccionEnvio);

        Assert.Equal(pedidoId, pedido.Id);
        Assert.Equal(EstadoPedido.Borrador, pedido.Estado);
        Assert.Equal(1, pedido.Version);
        Assert.Empty(pedido.Items);
    }

    // ---------------------------------------------------------------------
    // A2. Agregar un ítem con cantidad 0 u 11 lanza una excepción de dominio.
    // ---------------------------------------------------------------------
    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void AgregarItem_ConCantidadFueraDeRango_LanzaReglaNegocioException(int cantidad)
    {
        // Arrange
        var pedido = CrearPedidoEnBorrador();

        // Act
        var accion = () => pedido.AgregarItem("P001", cantidad, 45000m);

        // Assert
        Assert.Throws<ReglaNegocioException>(accion);
        Assert.Empty(pedido.Items);
        Assert.Single(pedido.EventosNuevos); // solo PedidoCreado: no se generó ItemAgregado
    }

    // Complementaria de A2: la suma de cantidades del mismo producto no puede superar 10.
    [Fact]
    public void AgregarItem_SumaDelMismoProductoSuperaDiez_LanzaReglaNegocioException()
    {
        // Arrange
        var pedido = CrearPedidoEnBorrador();
        pedido.AgregarItem("P001", 8, 45000m);

        // Act
        var accion = () => pedido.AgregarItem("P001", 3, 45000m);

        // Assert
        Assert.Throws<ReglaNegocioException>(accion);
        Assert.Single(pedido.Items);
    }

    // ---------------------------------------------------------------------
    // A3. Agregar un ítem a un pedido confirmado lanza una excepción.
    // ---------------------------------------------------------------------
    [Fact]
    public void AgregarItem_ConPedidoConfirmado_LanzaEstadoInvalidoException()
    {
        // Arrange
        var pedido = CrearPedidoEnBorrador();
        pedido.AgregarItem("P001", 2, 45000m);
        pedido.SolicitarConfirmacion(Guid.NewGuid());
        pedido.Confirmar("A7K29Q", "TN-000031", new DateOnly(2026, 11, 26));

        // Act
        var accion = () => pedido.AgregarItem("P002", 1, 18000m);

        // Assert
        var excepcion = Assert.Throws<EstadoInvalidoException>(accion);
        Assert.Equal(
            "Solo se pueden agregar ítems a un pedido en estado Borrador. Estado actual: Confirmado.",
            excepcion.Message);
    }

    // ---------------------------------------------------------------------
    // A4. El total aplica 10% de descuento al superar $100.000
    //     y no lo aplica en $100.000 exactos.
    // ---------------------------------------------------------------------
    [Fact]
    public void Total_SubtotalSuperaCienMil_AplicaDescuentoDelDiezPorCiento()
    {
        // Arrange
        var pedido = CrearPedidoEnBorrador();

        // Act
        pedido.AgregarItem("P001", 3, 45000m); // 135.000

        // Assert
        Assert.Equal(135000m, pedido.Subtotal);
        Assert.Equal(13500m, pedido.Descuento);
        Assert.Equal(121500m, pedido.Total);
    }

    [Fact]
    public void Total_SubtotalExactamenteCienMil_NoAplicaDescuento()
    {
        // Arrange
        var pedido = CrearPedidoEnBorrador();

        // Act
        pedido.AgregarItem("P006", 2, 50000m); // 100.000 exactos

        // Assert
        Assert.Equal(100000m, pedido.Subtotal);
        Assert.Equal(0m, pedido.Descuento);
        Assert.Equal(100000m, pedido.Total);
    }

    // ---------------------------------------------------------------------
    // A5. Confirmar un pedido sin ítems lanza una excepción.
    // ---------------------------------------------------------------------
    [Fact]
    public void SolicitarConfirmacion_SinItems_LanzaReglaNegocioException()
    {
        // Arrange
        var pedido = CrearPedidoEnBorrador();

        // Act
        var accion = () => pedido.SolicitarConfirmacion(Guid.NewGuid());

        // Assert
        Assert.Throws<ReglaNegocioException>(accion);
        Assert.Equal(EstadoPedido.Borrador, pedido.Estado);
    }

    // Complementaria de A5: un pedido solo se puede confirmar una vez.
    [Fact]
    public void SolicitarConfirmacion_PedidoYaEnConfirmacion_LanzaEstadoInvalidoException()
    {
        // Arrange
        var pedido = CrearPedidoEnBorrador();
        pedido.AgregarItem("P001", 2, 45000m);
        pedido.SolicitarConfirmacion(Guid.NewGuid());

        // Act
        var accion = () => pedido.SolicitarConfirmacion(Guid.NewGuid());

        // Assert
        Assert.Throws<EstadoInvalidoException>(accion);
    }

    // ---------------------------------------------------------------------
    // A6. Reconstruir el pedido desde una lista de eventos da el mismo estado
    //     que ejecutar los comandos.
    // ---------------------------------------------------------------------
    [Fact]
    public void Rehidratar_ConLosEventosGenerados_ReconstruyeElMismoEstado()
    {
        // Arrange: ejecutar los comandos
        var sagaId = Guid.NewGuid();
        var original = CrearPedidoEnBorrador();
        original.AgregarItem("P001", 2, 45000m);
        original.AgregarItem("P004", 1, 32000m);
        original.SolicitarConfirmacion(sagaId);
        original.Confirmar("A7K29Q", "TN-000031", new DateOnly(2026, 11, 26));

        // Act: reconstruir solo desde los eventos
        var rehidratado = Pedido.Rehidratar(original.EventosNuevos);

        // Assert
        Assert.Equal(original.Id, rehidratado.Id);
        Assert.Equal(original.ClienteId, rehidratado.ClienteId);
        Assert.Equal(original.DireccionEnvio, rehidratado.DireccionEnvio);
        Assert.Equal(original.Estado, rehidratado.Estado);
        Assert.Equal(original.Items, rehidratado.Items);
        Assert.Equal(original.Subtotal, rehidratado.Subtotal);
        Assert.Equal(original.Descuento, rehidratado.Descuento);
        Assert.Equal(original.Total, rehidratado.Total);
        Assert.Equal(original.Version, rehidratado.Version);
        Assert.Equal(original.SagaId, rehidratado.SagaId);
        Assert.Equal(original.CodigoAutorizacion, rehidratado.CodigoAutorizacion);
        Assert.Equal(original.CodigoSeguimiento, rehidratado.CodigoSeguimiento);
        Assert.Equal(original.FechaEstimadaEntrega, rehidratado.FechaEstimadaEntrega);

        Assert.Equal(5, rehidratado.Version);
        Assert.Equal(EstadoPedido.Confirmado, rehidratado.Estado);
        Assert.Empty(rehidratado.EventosNuevos); // rehidratar no genera eventos nuevos
    }
}
