using Pedidos.Application.Excepciones;
using Pedidos.Application.Puertos;
using Pedidos.Application.Sagas;
using Pedidos.Domain.Eventos;
using TiendaNorte.Contracts;

namespace TiendaNorte.UnitTests.Dobles;

// =============================================================================
// Dobles de prueba de los puertos de Pedidos (Hito 5).
// Reemplazan a PostgreSQL y RabbitMQ: guardan todo en memoria y registran
// cada llamada para que las pruebas puedan verificarla. Sin HTTP, sin base, sin colas.
// =============================================================================

/// <summary>Event store en memoria: un stream de eventos por pedido.</summary>
public sealed class FakeEventStore : IEventStore
{
    private readonly Dictionary<Guid, List<EventoPedido>> _streams = [];

    public Task<IReadOnlyList<EventoPedido>> LeerAsync(Guid pedidoId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EventoPedido>>(
            _streams.TryGetValue(pedidoId, out var eventos) ? eventos.ToList() : []);

    public Task<IReadOnlyList<EventoGuardado>> LeerFilasAsync(Guid pedidoId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EventoGuardado>>(
            _streams.TryGetValue(pedidoId, out var eventos)
                ? eventos.Select(e => new EventoGuardado(e.Version, e.GetType().Name, e.FechaUtc, "{}")).ToList()
                : []);

    public Task GuardarAsync(Guid pedidoId, int versionEsperada, IReadOnlyList<EventoPedido> eventosNuevos, CancellationToken ct = default)
    {
        var stream = _streams.TryGetValue(pedidoId, out var existente) ? existente : _streams[pedidoId] = [];

        // Igual que UNIQUE (stream_id, version): si otro guardó antes, conflicto
        if (stream.Count != versionEsperada)
            throw new ConcurrenciaException($"Versión esperada {versionEsperada}, actual {stream.Count}.");

        stream.AddRange(eventosNuevos);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Repositorio de sagas en memoria. Registra una "foto" del estado cada vez que se guarda,
/// para verificar que la saga se persiste con cada evento (prueba B6).
/// </summary>
public sealed class FakeSagaRepository : ISagaRepository
{
    private readonly Dictionary<Guid, SagaConfirmacion> _sagas = [];
    private readonly HashSet<Guid> _mensajesProcesados = [];
    private long _ultimoIdPaso;

    /// <summary>Estado y paso actual de la saga en cada ActualizarAsync, en orden.</summary>
    public List<(string Estado, string PasoActual)> Guardados { get; } = [];

    public Task<SagaConfirmacion?> ObtenerAsync(Guid sagaId, CancellationToken ct = default) =>
        Task.FromResult(_sagas.GetValueOrDefault(sagaId));

    public Task CrearAsync(SagaConfirmacion saga, CancellationToken ct = default)
    {
        AsignarIds(saga);
        _sagas[saga.SagaId] = saga;
        return Task.CompletedTask;
    }

    public Task ActualizarAsync(SagaConfirmacion saga, CancellationToken ct = default)
    {
        AsignarIds(saga);
        _sagas[saga.SagaId] = saga;
        Guardados.Add((saga.Estado, saga.PasoActual));
        return Task.CompletedTask;
    }

    public Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default) =>
        Task.FromResult(_mensajesProcesados.Contains(messageId));

    public Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default)
    {
        _mensajesProcesados.Add(messageId);
        return Task.CompletedTask;
    }

    private void AsignarIds(SagaConfirmacion saga)
    {
        foreach (var paso in saga.Pasos.Where(p => p.Id == 0))
            paso.Id = ++_ultimoIdPaso;
    }
}

/// <summary>Unidad de trabajo que solo cuenta las llamadas.</summary>
public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int Confirmadas { get; private set; }
    public int Deshechas { get; private set; }

    public Task IniciarAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task ConfirmarAsync(CancellationToken ct = default)
    {
        Confirmadas++;
        return Task.CompletedTask;
    }

    public Task DeshacerAsync(CancellationToken ct = default)
    {
        Deshechas++;
        return Task.CompletedTask;
    }
}

/// <summary>Publicador que guarda, EN ORDEN, cada mensaje con su cola.</summary>
public sealed class FakeMessagePublisher : IMessagePublisher
{
    public List<(string Cola, object Mensaje, string Tipo)> Publicados { get; } = [];

    public Task PublicarAsync<TDatos>(string cola, Mensaje<TDatos> mensaje, CancellationToken ct = default)
    {
        Publicados.Add((cola, mensaje!, mensaje.Tipo));
        return Task.CompletedTask;
    }

    /// <summary>Tipos publicados en orden, por ejemplo ["ReservarStock", "Cobrar"].</summary>
    public IReadOnlyList<string> Tipos => Publicados.Select(p => p.Tipo).ToList();

    /// <summary>El último mensaje publicado de un tipo de comando.</summary>
    public Mensaje<T> Ultimo<T>() => Publicados.Select(p => p.Mensaje).OfType<Mensaje<T>>().Last();
}
