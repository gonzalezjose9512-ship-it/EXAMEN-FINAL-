using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Pagos.Service.Handlers;
using Pagos.Service.Reglas;
using TiendaNorte.Contracts;
using TiendaNorte.UnitTests.Dobles;

namespace TiendaNorte.UnitTests.Servicios;

/// <summary>
/// Grupo C — CobrarHandler con dobles del repositorio y del publicador (C2).
/// La fecha se fija con FakeTimeProvider: el vencimiento de la tarjeta depende de "hoy".
/// </summary>
public class PagosHandlersTests
{
    private readonly FakePagosRepository _repositorio = new();
    private readonly FakeRespuestaPublisher _publisher = new();
    private readonly FakeTimeProvider _tiempo = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));

    private CobrarHandler CrearHandler() =>
        new(_repositorio, _publisher, new ValidadorDePago(_tiempo), _tiempo, NullLogger<CobrarHandler>.Instance);

    private static Mensaje<Cobrar> ComandoCobrar(decimal monto, string numero, string vencimiento = "12/28") =>
        Mensaje.Crear(Guid.NewGuid(), Guid.NewGuid(),
            new Cobrar(monto, new DatosTarjeta(numero, "Juan Perez", vencimiento)),
            new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc));

    // ---------------------------------------------------------------------
    // C2. Cobrar con una tarjeta terminada en 0000 publica PagoRechazado.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_TarjetaTerminadaEn0000_PublicaPagoRechazado()
    {
        // Arrange
        var comando = ComandoCobrar(90000m, "4111111111110000");

        // Act
        await CrearHandler().ManejarAsync(comando);

        // Assert
        var rechazo = _publisher.UnicoPublicado<PagoRechazado>();
        Assert.Equal(MotivosRechazo.TarjetaRechazada, rechazo.Motivo);
        Assert.Equal("PG-000001", rechazo.PagoId); // el rechazo también tiene pagoId

        var pago = Assert.Single(_repositorio.Registrados);
        Assert.Equal("Rechazado", pago.Estado);
        Assert.Equal("0000", pago.TarjetaUltimos4); // solo los últimos 4, nunca el número completo
    }

    // Complementaria: regla que depende de la fecha, probada con FakeTimeProvider.
    [Fact]
    public async Task ManejarAsync_TarjetaVencida_PublicaPagoRechazadoPorDatosInvalidos()
    {
        // Arrange: hoy es 17/11/2026 y la tarjeta venció en octubre de 2026
        var comando = ComandoCobrar(90000m, "4111111111111111", vencimiento: "10/26");

        // Act
        await CrearHandler().ManejarAsync(comando);

        // Assert
        var rechazo = _publisher.UnicoPublicado<PagoRechazado>();
        Assert.Equal(MotivosRechazo.DatosInvalidos, rechazo.Motivo);
        Assert.Equal("Tarjeta vencida", rechazo.Detalle);
    }

    // Complementaria: el orden de las reglas. Con 0000 Y monto excedido, gana TARJETA_RECHAZADA.
    [Fact]
    public void Validar_TarjetaRechazadaYMontoExcedido_DevuelveElPrimerMotivo()
    {
        // Arrange
        var validador = new ValidadorDePago(_tiempo);

        // Act
        var rechazo = validador.Validar(600000m, new DatosTarjeta("4111111111110000", "Juan Perez", "12/28"));

        // Assert
        Assert.Equal(MotivosRechazo.TarjetaRechazada, rechazo!.Motivo);
    }

    // Complementaria: pago aprobado con código de autorización de 6 caracteres alfanuméricos.
    [Fact]
    public async Task ManejarAsync_TarjetaValida_PublicaPagoAprobadoConCodigoDeSeisCaracteres()
    {
        // Arrange
        var comando = ComandoCobrar(90000m, "4111111111111111");

        // Act
        await CrearHandler().ManejarAsync(comando);

        // Assert
        var aprobado = _publisher.UnicoPublicado<PagoAprobado>();
        Assert.Equal(90000m, aprobado.Monto);
        Assert.Equal(6, aprobado.CodigoAutorizacion.Length);
        Assert.True(aprobado.CodigoAutorizacion.All(char.IsAsciiLetterOrDigit));
        Assert.Equal("1111", Assert.Single(_repositorio.Registrados).TarjetaUltimos4);
    }
}
