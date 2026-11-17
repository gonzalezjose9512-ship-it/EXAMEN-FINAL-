using Microsoft.Extensions.Time.Testing;
using Pedidos.Application.CasosDeUso;
using Pedidos.Application.Sagas;
using Pedidos.Domain;
using TiendaNorte.Contracts;
using TiendaNorte.UnitTests.Dobles;

namespace TiendaNorte.UnitTests.Saga;

/// <summary>
/// Arma todo lo necesario para probar la saga con dobles: un pedido en el event store,
/// el caso de uso ConfirmarPedido y ConfirmacionPedidoSaga. Permite "responder" eventos
/// como si fueran los servicios, sin RabbitMQ ni PostgreSQL.
/// </summary>
public sealed class EscenarioSaga
{
    public FakeEventStore EventStore { get; } = new();
    public FakeSagaRepository SagaRepository { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeMessagePublisher Publisher { get; } = new();
    public FakeTimeProvider Tiempo { get; } = new(new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));

    public ConfirmarPedido ConfirmarPedido { get; }
    public ConfirmacionPedidoSaga Saga { get; }

    public Guid PedidoId { get; } = Guid.NewGuid();
    public Guid SagaId { get; private set; }

    public EscenarioSaga()
    {
        ConfirmarPedido = new ConfirmarPedido(EventStore, SagaRepository, UnitOfWork, Publisher, Tiempo);
        Saga = new ConfirmacionPedidoSaga(SagaRepository, EventStore, UnitOfWork, Publisher, Tiempo);
    }

    /// <summary>Crea el pedido con 2 × P001 ($90.000) y lo deja guardado en el event store.</summary>
    public async Task CrearPedidoAsync()
    {
        var pedido = Pedido.Crear(PedidoId, "CLI-001", new Direccion("Av. Mate de Luna", "1234", "San Miguel de Tucumán", "4000"));
        pedido.AgregarItem("P001", 2, 45000m);
        await EventStore.GuardarAsync(PedidoId, 0, pedido.EventosNuevos);
    }

    /// <summary>Crea el pedido y ejecuta ConfirmarPedido (publica ReservarStock).</summary>
    public async Task ConfirmarAsync()
    {
        await CrearPedidoAsync();
        var tarjeta = new DatosTarjeta("4111111111111111", "Juan Perez", "12/28");
        var resultado = await ConfirmarPedido.EjecutarAsync(PedidoId, tarjeta, new DateOnly(2026, 11, 23));
        SagaId = resultado.SagaId;
    }

    /// <summary>Simula la respuesta de un servicio: un evento de pedidos.respuestas con un messageId nuevo.</summary>
    public Task<ResultadoSaga> ResponderAsync<TEvento>(TEvento evento) where TEvento : notnull =>
        Saga.ManejarAsync(Mensaje.Crear(SagaId, PedidoId, evento, Tiempo.GetUtcNow().UtcDateTime));

    /// <summary>Estado actual del pedido, reconstruido desde sus eventos.</summary>
    public async Task<Pedido> PedidoActualAsync() =>
        Pedido.Rehidratar(await EventStore.LeerAsync(PedidoId));

    // Respuestas típicas de los servicios
    public static StockReservado StockReservado() => new("R-000001", [new ItemStock("P001", 2)]);
    public static PagoAprobado PagoAprobado() => new("PG-000001", 90000m, "A7K29Q");
    public static EnvioProgramado EnvioProgramado() =>
        new("E-000001", "TN-000001", new DateOnly(2026, 11, 23), new DateOnly(2026, 11, 26));
}
