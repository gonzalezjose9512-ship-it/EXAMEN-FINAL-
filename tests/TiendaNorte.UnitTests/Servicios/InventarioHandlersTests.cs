using Inventario.Service.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TiendaNorte.Contracts;
using TiendaNorte.UnitTests.Dobles;

namespace TiendaNorte.UnitTests.Servicios;

/// <summary>
/// Grupo C — Handlers de Inventario con dobles del repositorio y del publicador (C1 y C4).
/// </summary>
public class InventarioHandlersTests
{
    private readonly FakeInventarioRepository _repositorio = new();
    private readonly FakeRespuestaPublisher _publisher = new();
    private readonly FakeTimeProvider _tiempo = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));

    private ReservarStockHandler CrearReservarHandler() =>
        new(_repositorio, _publisher, _tiempo, NullLogger<ReservarStockHandler>.Instance);

    private static Mensaje<ReservarStock> ComandoReservar(params ItemStock[] items) =>
        Mensaje.Crear(Guid.NewGuid(), Guid.NewGuid(), new ReservarStock(items), new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc));

    // ---------------------------------------------------------------------
    // C1. ReservarStock con más unidades que el stock publica StockRechazado y el stock no cambia.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_MasUnidadesQueElStock_PublicaStockRechazadoYNoCambiaElStock()
    {
        // Arrange: P005 tiene stock 2
        _repositorio.Stock["P005"] = 2;
        var comando = ComandoReservar(new ItemStock("P005", 3));

        // Act
        await CrearReservarHandler().ManejarAsync(comando);

        // Assert
        var rechazo = _publisher.UnicoPublicado<StockRechazado>();
        Assert.Equal(MotivosRechazo.StockInsuficiente, rechazo.Motivo);
        Assert.Equal("P005", rechazo.ProductoId);
        Assert.Equal(3, rechazo.Solicitado);
        Assert.Equal(2, rechazo.Disponible);

        Assert.Equal(2, _repositorio.Stock["P005"]); // el stock no cambió
        Assert.Empty(_repositorio.Reservas);          // no se creó ninguna reserva
    }

    // ---------------------------------------------------------------------
    // C4. Idempotencia: procesar dos veces el mismo MessageId de ReservarStock
    //     descuenta el stock una sola vez.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_MismoMessageIdDosVeces_DescuentaElStockUnaSolaVez()
    {
        // Arrange
        _repositorio.Stock["P001"] = 10;
        var comando = ComandoReservar(new ItemStock("P001", 2));
        var handler = CrearReservarHandler();

        // Act: el mismo mensaje llega dos veces (RabbitMQ entrega "al menos una vez")
        await handler.ManejarAsync(comando);
        await handler.ManejarAsync(comando);

        // Assert
        Assert.Equal(8, _repositorio.Stock["P001"]);                       // 10 - 2, una sola vez
        Assert.Single(_repositorio.Reservas);                              // una sola reserva
        Assert.Equal("R-000001", _publisher.UnicoPublicado<StockReservado>().ReservaId); // una sola respuesta
    }

    // Complementaria: se revisan TODOS los ítems antes de modificar nada.
    [Fact]
    public async Task ManejarAsync_UnProductoInexistente_RechazaSinTocarElStockDeLosDemas()
    {
        // Arrange: P001 existe y alcanza; P999 no existe
        _repositorio.Stock["P001"] = 10;
        var comando = ComandoReservar(new ItemStock("P001", 2), new ItemStock("P999", 1));

        // Act
        await CrearReservarHandler().ManejarAsync(comando);

        // Assert
        var rechazo = _publisher.UnicoPublicado<StockRechazado>();
        Assert.Equal(MotivosRechazo.ProductoInexistente, rechazo.Motivo);
        Assert.Equal("P999", rechazo.ProductoId);
        Assert.Equal(10, _repositorio.Stock["P001"]); // P001 tampoco se tocó
    }

    // Complementaria: la compensación devuelve el stock reservado.
    [Fact]
    public async Task LiberarStock_ReservaActiva_DevuelveElStockYPublicaStockLiberado()
    {
        // Arrange: una reserva de 2 × P001 (stock 10 → 8)
        _repositorio.Stock["P001"] = 10;
        await CrearReservarHandler().ManejarAsync(ComandoReservar(new ItemStock("P001", 2)));
        var liberar = Mensaje.Crear(Guid.NewGuid(), Guid.NewGuid(), new LiberarStock("R-000001"), DateTime.UtcNow);
        var handler = new LiberarStockHandler(_repositorio, _publisher, _tiempo, NullLogger<LiberarStockHandler>.Instance);

        // Act
        await handler.ManejarAsync(liberar);

        // Assert
        Assert.Equal(10, _repositorio.Stock["P001"]);
        Assert.Equal("Liberada", _repositorio.Reservas["R-000001"].Estado);
        Assert.Equal("R-000001", _publisher.UnicoPublicado<StockLiberado>().ReservaId);
    }
}
