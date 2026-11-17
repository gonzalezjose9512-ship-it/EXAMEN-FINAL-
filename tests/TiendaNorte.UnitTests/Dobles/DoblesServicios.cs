using Logistica.Service.Abstracciones;
using Pagos.Service.Abstracciones;
using TiendaNorte.Contracts;
using IRespuestaInventario = Inventario.Service.Abstracciones.IRespuestaPublisher;
using IRespuestaPagos = Pagos.Service.Abstracciones.IRespuestaPublisher;
using IRespuestaLogistica = Logistica.Service.Abstracciones.IRespuestaPublisher;

namespace TiendaNorte.UnitTests.Dobles;

// =============================================================================
// Dobles de prueba de los repositorios y publicadores de los 3 servicios (grupo C).
// =============================================================================

/// <summary>
/// Publicador de respuestas en memoria. Implementa las 3 interfaces IRespuestaPublisher
/// (una por servicio): tienen el mismo método, así que un solo doble sirve para todos.
/// </summary>
public sealed class FakeRespuestaPublisher : IRespuestaInventario, IRespuestaPagos, IRespuestaLogistica
{
    public List<object> Publicados { get; } = [];

    public Task PublicarAsync<TDatos>(Mensaje<TDatos> mensaje, CancellationToken ct = default)
    {
        Publicados.Add(mensaje!);
        return Task.CompletedTask;
    }

    /// <summary>Los datos del único mensaje publicado, del tipo indicado.</summary>
    public T UnicoPublicado<T>() => Assert.Single(Publicados.OfType<Mensaje<T>>()).Datos;
}

/// <summary>Inventario en memoria: stock por producto, reservas y mensajes procesados.</summary>
public sealed class FakeInventarioRepository : Inventario.Service.Abstracciones.IInventarioRepository
{
    private readonly HashSet<Guid> _procesados = [];
    private int _ultimaReserva;

    public Dictionary<string, int> Stock { get; } = [];
    public Dictionary<string, (string Estado, IReadOnlyList<ItemStock> Items)> Reservas { get; } = [];

    public Task IniciarTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ConfirmarTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DeshacerTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Task.FromResult(_procesados.Contains(messageId));

    public Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default)
    {
        _procesados.Add(messageId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, int>> ObtenerStockBloqueandoAsync(IReadOnlyCollection<string> productoIds, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<string, int>>(
            Stock.Where(s => productoIds.Contains(s.Key)).ToDictionary(s => s.Key, s => s.Value));

    public Task<string> CrearReservaAsync(Guid pedidoId, Guid sagaId, IReadOnlyList<ItemStock> items, CancellationToken ct = default)
    {
        foreach (var item in items)
            Stock[item.ProductoId] -= item.Cantidad;

        var reservaId = $"R-{++_ultimaReserva:D6}";
        Reservas[reservaId] = ("Activa", items);
        return Task.FromResult(reservaId);
    }

    public Task<string?> ObtenerEstadoReservaBloqueandoAsync(string reservaId, CancellationToken ct = default) =>
        Task.FromResult(Reservas.TryGetValue(reservaId, out var r) ? r.Estado : null);

    public Task LiberarReservaAsync(string reservaId, CancellationToken ct = default)
    {
        var (_, items) = Reservas[reservaId];
        foreach (var item in items)
            Stock[item.ProductoId] += item.Cantidad;
        Reservas[reservaId] = ("Liberada", items);
        return Task.CompletedTask;
    }
}

/// <summary>Pagos en memoria: registra cada intento de cobro.</summary>
public sealed class FakePagosRepository : IPagosRepository
{
    private readonly HashSet<Guid> _procesados = [];

    public List<NuevoPago> Registrados { get; } = [];

    public Task IniciarTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ConfirmarTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DeshacerTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Task.FromResult(_procesados.Contains(messageId));

    public Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default)
    {
        _procesados.Add(messageId);
        return Task.CompletedTask;
    }

    public Task<string> RegistrarPagoAsync(NuevoPago pago, CancellationToken ct = default)
    {
        Registrados.Add(pago);
        return Task.FromResult($"PG-{Registrados.Count:D6}");
    }

    public Task<PagoBloqueado?> ObtenerPagoBloqueandoAsync(string pagoId, CancellationToken ct = default) =>
        Task.FromResult<PagoBloqueado?>(null);

    public Task MarcarReembolsadoAsync(string pagoId, DateTime fechaReembolsoUtc, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>Logística en memoria: cuántos envíos programados hay por fecha y cuáles se crearon.</summary>
public sealed class FakeEnviosRepository : IEnviosRepository
{
    private readonly HashSet<Guid> _procesados = [];

    public Dictionary<DateOnly, int> ProgramadosPorFecha { get; } = [];
    public List<NuevoEnvio> Creados { get; } = [];

    public Task IniciarTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task ConfirmarTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task DeshacerTransaccionAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Task.FromResult(_procesados.Contains(messageId));

    public Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default)
    {
        _procesados.Add(messageId);
        return Task.CompletedTask;
    }

    public Task<int> ContarProgramadosAsync(DateOnly fechaDespacho, CancellationToken ct = default) =>
        Task.FromResult(ProgramadosPorFecha.GetValueOrDefault(fechaDespacho));

    public Task<EnvioCreado> CrearEnvioAsync(NuevoEnvio envio, CancellationToken ct = default)
    {
        Creados.Add(envio);
        ProgramadosPorFecha[envio.FechaDespacho] = ProgramadosPorFecha.GetValueOrDefault(envio.FechaDespacho) + 1;
        var numero = Creados.Count;
        return Task.FromResult(new EnvioCreado($"E-{numero:D6}", $"TN-{numero:D6}"));
    }

    public Task<string?> ObtenerEstadoEnvioBloqueandoAsync(string envioId, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task AnularEnvioAsync(string envioId, CancellationToken ct = default) => Task.CompletedTask;
}
