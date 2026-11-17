using Logistica.Service.Handlers;
using Logistica.Service.Reglas;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using TiendaNorte.Contracts;
using TiendaNorte.UnitTests.Dobles;

namespace TiendaNorte.UnitTests.Servicios;

/// <summary>
/// Grupo C — ProgramarEnvioHandler con dobles del repositorio y del publicador (C3).
/// "Hoy" se fija con FakeTimeProvider para que el cálculo de días hábiles sea predecible.
/// </summary>
public class LogisticaHandlersTests
{
    private static readonly DireccionEnvio DireccionCompleta =
        new("Av. Mate de Luna", "1234", "San Miguel de Tucumán", "4000");

    private readonly FakeEnviosRepository _repositorio = new();
    private readonly FakeRespuestaPublisher _publisher = new();

    // Viernes 20/11/2026
    private readonly FakeTimeProvider _tiempo = new(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));

    private ProgramarEnvioHandler CrearHandler(int cupoDiario = 3) =>
        new(_repositorio, _publisher, new CalendarioHabil(_tiempo),
            Options.Create(new OpcionesLogistica { CupoDiario = cupoDiario }),
            _tiempo, NullLogger<ProgramarEnvioHandler>.Instance);

    private static Mensaje<ProgramarEnvio> ComandoProgramar(DateOnly? fechaPreferida, DireccionEnvio? direccion = null) =>
        Mensaje.Crear(Guid.NewGuid(), Guid.NewGuid(),
            new ProgramarEnvio(direccion ?? DireccionCompleta, fechaPreferida),
            new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

    // ---------------------------------------------------------------------
    // C3. ProgramarEnvio con el cupo diario completo publica EnvioRechazado.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_CupoDiarioCompleto_PublicaEnvioRechazado()
    {
        // Arrange: el lunes 23/11 ya tiene 3 envíos programados y el cupo es 3
        var lunes = new DateOnly(2026, 11, 23);
        _repositorio.ProgramadosPorFecha[lunes] = 3;

        // Act
        await CrearHandler(cupoDiario: 3).ManejarAsync(ComandoProgramar(lunes));

        // Assert
        var rechazo = _publisher.UnicoPublicado<EnvioRechazado>();
        Assert.Equal(MotivosRechazo.SinCupo, rechazo.Motivo);
        Assert.Equal(lunes, rechazo.Fecha);
        Assert.Equal(3, rechazo.CupoDiario);
        Assert.Empty(_repositorio.Creados); // no se creó el envío
    }

    // Complementaria: la fecha preferida no puede caer en fin de semana.
    [Fact]
    public async Task ManejarAsync_FechaPreferidaEnSabado_PublicaEnvioRechazadoFechaInvalida()
    {
        // Arrange
        var sabado = new DateOnly(2026, 11, 21);

        // Act
        await CrearHandler().ManejarAsync(ComandoProgramar(sabado));

        // Assert
        var rechazo = _publisher.UnicoPublicado<EnvioRechazado>();
        Assert.Equal(MotivosRechazo.FechaInvalida, rechazo.Motivo);
        Assert.Equal(sabado, rechazo.Fecha);
    }

    // Complementaria: dirección incompleta.
    [Fact]
    public async Task ManejarAsync_DireccionSinCodigoPostal_PublicaEnvioRechazadoDireccionInvalida()
    {
        // Arrange
        var direccion = DireccionCompleta with { CodigoPostal = null };

        // Act
        await CrearHandler().ManejarAsync(ComandoProgramar(null, direccion));

        // Assert
        var rechazo = _publisher.UnicoPublicado<EnvioRechazado>();
        Assert.Equal(MotivosRechazo.DireccionInvalida, rechazo.Motivo);
        Assert.Equal("Falta codigoPostal", rechazo.Detalle);
    }

    // Complementaria: sin fecha preferida, despacho el próximo día hábil y entrega + 3 días hábiles.
    [Fact]
    public async Task ManejarAsync_SinFechaPreferidaUnViernes_ProgramaParaElLunesYEntregaElJueves()
    {
        // Arrange: hoy es viernes 20/11 → mañana es sábado → próximo hábil: lunes 23/11

        // Act
        await CrearHandler().ManejarAsync(ComandoProgramar(null));

        // Assert
        var programado = _publisher.UnicoPublicado<EnvioProgramado>();
        Assert.Equal(new DateOnly(2026, 11, 23), programado.FechaDespacho);
        Assert.Equal(new DateOnly(2026, 11, 26), programado.FechaEstimadaEntrega);
        Assert.Equal("E-000001", programado.EnvioId);
        Assert.Equal("TN-000001", programado.CodigoSeguimiento);
    }
}
