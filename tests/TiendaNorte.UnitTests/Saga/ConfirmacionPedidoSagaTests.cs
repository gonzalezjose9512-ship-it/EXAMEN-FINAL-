using Pedidos.Application.Sagas;
using Pedidos.Domain;
using TiendaNorte.Contracts;

namespace TiendaNorte.UnitTests.Saga;

/// <summary>
/// Grupo B — Saga de confirmación (mínimo 6 pruebas).
/// Los puertos se reemplazan por dobles: sin PostgreSQL, RabbitMQ ni HTTP.
/// Nombres con el patrón Metodo_Escenario_ResultadoEsperado y estructura Arrange / Act / Assert.
/// </summary>
public class ConfirmacionPedidoSagaTests
{
    // ---------------------------------------------------------------------
    // B1. ConfirmarPedido publica ReservarStock con el SagaId;
    //     luego la saga, al recibir StockReservado, publica Cobrar.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ConfirmarPedido_YLuegoStockReservado_PublicaReservarStockConSagaIdYDespuesCobrar()
    {
        // Arrange
        var escenario = new EscenarioSaga();

        // Act 1: confirmar el pedido
        await escenario.ConfirmarAsync();

        // Assert 1: ReservarStock en inventario.comandos, con el SagaId de la saga creada
        var (cola, _, tipo) = Assert.Single(escenario.Publisher.Publicados);
        Assert.Equal(Topologia.ColaInventario, cola);
        Assert.Equal(nameof(ReservarStock), tipo);
        var reservar = escenario.Publisher.Ultimo<ReservarStock>();
        Assert.Equal(escenario.SagaId, reservar.SagaId);
        Assert.Equal(escenario.PedidoId, reservar.PedidoId);

        // Act 2: Inventario responde StockReservado
        await escenario.ResponderAsync(EscenarioSaga.StockReservado());

        // Assert 2: Cobrar en pagos.comandos, con el mismo SagaId y el total del pedido
        Assert.Equal(new[] { nameof(ReservarStock), nameof(Cobrar) }, escenario.Publisher.Tipos);
        Assert.Equal(Topologia.ColaPagos, escenario.Publisher.Publicados[1].Cola);
        var cobrar = escenario.Publisher.Ultimo<Cobrar>();
        Assert.Equal(escenario.SagaId, cobrar.SagaId);
        Assert.Equal(90000m, cobrar.Datos.Monto);
    }

    // ---------------------------------------------------------------------
    // B2. Al recibir StockRechazado el pedido queda Rechazado y no se publican más comandos.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_StockRechazado_PedidoRechazadoYNoPublicaMasComandos()
    {
        // Arrange
        var escenario = new EscenarioSaga();
        await escenario.ConfirmarAsync();

        // Act
        await escenario.ResponderAsync(new StockRechazado(MotivosRechazo.StockInsuficiente, "P001", 2, 1));

        // Assert
        var pedido = await escenario.PedidoActualAsync();
        Assert.Equal(EstadoPedido.Rechazado, pedido.Estado);
        Assert.Equal(MotivosRechazo.StockInsuficiente, pedido.MotivoRechazo);

        Assert.Equal(new[] { nameof(ReservarStock) }, escenario.Publisher.Tipos); // solo el comando inicial

        var saga = await escenario.SagaRepository.ObtenerAsync(escenario.SagaId);
        Assert.Equal(EstadosSaga.Fallida, saga!.Estado);
    }

    // ---------------------------------------------------------------------
    // B3. Al recibir PagoRechazado se publica LiberarStock y no se publica ProgramarEnvio.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_PagoRechazado_PublicaLiberarStockYNoProgramarEnvio()
    {
        // Arrange
        var escenario = new EscenarioSaga();
        await escenario.ConfirmarAsync();
        await escenario.ResponderAsync(EscenarioSaga.StockReservado());

        // Act
        await escenario.ResponderAsync(new PagoRechazado("PG-000001", MotivosRechazo.TarjetaRechazada));

        // Assert
        Assert.Equal(nameof(LiberarStock), escenario.Publisher.Tipos[^1]);
        Assert.Equal(Topologia.ColaInventario, escenario.Publisher.Publicados[^1].Cola);
        Assert.Equal("R-000001", escenario.Publisher.Ultimo<LiberarStock>().Datos.ReservaId);
        Assert.DoesNotContain(nameof(ProgramarEnvio), escenario.Publisher.Tipos);

        var saga = await escenario.SagaRepository.ObtenerAsync(escenario.SagaId);
        Assert.Equal(EstadosSaga.Compensando, saga!.Estado);
        Assert.Null(saga.Datos.Tarjeta); // la tarjeta se borra al recibir la respuesta de Pagos
    }

    // ---------------------------------------------------------------------
    // B4. ProgramarEnvio se publica solo después de recibir PagoAprobado.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_ProgramarEnvio_SoloSePublicaDespuesDePagoAprobado()
    {
        // Arrange
        var escenario = new EscenarioSaga();
        await escenario.ConfirmarAsync();

        // Act 1: después de StockReservado todavía no corresponde programar el envío
        await escenario.ResponderAsync(EscenarioSaga.StockReservado());

        // Assert 1
        Assert.DoesNotContain(nameof(ProgramarEnvio), escenario.Publisher.Tipos);

        // Act 2: Pagos aprueba
        await escenario.ResponderAsync(EscenarioSaga.PagoAprobado());

        // Assert 2: recién ahora, y como último comando
        Assert.Equal(new[] { nameof(ReservarStock), nameof(Cobrar), nameof(ProgramarEnvio) }, escenario.Publisher.Tipos);
        Assert.Equal(Topologia.ColaLogistica, escenario.Publisher.Publicados[^1].Cola);
        Assert.Equal(new DateOnly(2026, 11, 23), escenario.Publisher.Ultimo<ProgramarEnvio>().Datos.FechaPreferida);
    }

    // ---------------------------------------------------------------------
    // B5. Al recibir EnvioRechazado se publica Reembolsar ANTES que LiberarStock (orden inverso).
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_EnvioRechazado_PublicaReembolsarAntesQueLiberarStock()
    {
        // Arrange: stock reservado y pago aprobado
        var escenario = new EscenarioSaga();
        await escenario.ConfirmarAsync();
        await escenario.ResponderAsync(EscenarioSaga.StockReservado());
        await escenario.ResponderAsync(EscenarioSaga.PagoAprobado());

        // Act 1: Logística rechaza por falta de cupo
        await escenario.ResponderAsync(new EnvioRechazado(MotivosRechazo.SinCupo, new DateOnly(2026, 11, 23), 3));

        // Assert 1: primero se compensa el pago (lo último que se completó)
        Assert.Equal(nameof(Reembolsar), escenario.Publisher.Tipos[^1]);
        Assert.DoesNotContain(nameof(LiberarStock), escenario.Publisher.Tipos);

        // Act 2: Pagos confirma el reembolso
        await escenario.ResponderAsync(new PagoReembolsado("PG-000001", 90000m));

        // Assert 2: recién después se libera el stock
        var tipos = escenario.Publisher.Tipos;
        Assert.True(tipos.ToList().IndexOf(nameof(Reembolsar)) < tipos.ToList().IndexOf(nameof(LiberarStock)));
        Assert.Equal(nameof(LiberarStock), tipos[^1]);

        // Act 3: Inventario confirma la liberación
        await escenario.ResponderAsync(new StockLiberado("R-000001"));

        // Assert 3: saga Compensada y pedido Rechazado con el motivo original
        var saga = await escenario.SagaRepository.ObtenerAsync(escenario.SagaId);
        Assert.Equal(EstadosSaga.Compensada, saga!.Estado);
        Assert.Equal(
            new[] { PasosSaga.ReservarStock, PasosSaga.Cobrar, PasosSaga.ProgramarEnvio, PasosSaga.Reembolsar, PasosSaga.LiberarStock },
            saga.Pasos.Select(p => p.Nombre));

        var pedido = await escenario.PedidoActualAsync();
        Assert.Equal(EstadoPedido.Rechazado, pedido.Estado);
        Assert.Equal(MotivosRechazo.SinCupo, pedido.MotivoRechazo);
    }

    // ---------------------------------------------------------------------
    // B6. El estado de la saga se guarda en ISagaRepository cada vez que llega un evento.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_CadaEvento_GuardaElEstadoDeLaSaga()
    {
        // Arrange
        var escenario = new EscenarioSaga();
        await escenario.ConfirmarAsync();

        // Act: el camino feliz completo
        await escenario.ResponderAsync(EscenarioSaga.StockReservado());
        await escenario.ResponderAsync(EscenarioSaga.PagoAprobado());
        await escenario.ResponderAsync(EscenarioSaga.EnvioProgramado());

        // Assert: un guardado por evento, con el estado de ese momento
        Assert.Equal(
            new[]
            {
                (EstadosSaga.EnCurso, PasosSaga.Cobrar),
                (EstadosSaga.EnCurso, PasosSaga.ProgramarEnvio),
                (EstadosSaga.Completada, PasosSaga.ProgramarEnvio)
            },
            escenario.SagaRepository.Guardados);

        // Y cada evento se guardó en su propia transacción (+1 de ConfirmarPedido)
        Assert.Equal(4, escenario.UnitOfWork.Confirmadas);
    }

    // ---------------------------------------------------------------------
    // Complementaria: el camino feliz deja el pedido Confirmado con los datos de los servicios.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_CaminoFeliz_PedidoConfirmadoConCodigos()
    {
        // Arrange
        var escenario = new EscenarioSaga();
        await escenario.ConfirmarAsync();

        // Act
        await escenario.ResponderAsync(EscenarioSaga.StockReservado());
        await escenario.ResponderAsync(EscenarioSaga.PagoAprobado());
        await escenario.ResponderAsync(EscenarioSaga.EnvioProgramado());

        // Assert
        var pedido = await escenario.PedidoActualAsync();
        Assert.Equal(EstadoPedido.Confirmado, pedido.Estado);
        Assert.Equal("A7K29Q", pedido.CodigoAutorizacion);
        Assert.Equal("TN-000001", pedido.CodigoSeguimiento);
        Assert.Equal(new DateOnly(2026, 11, 26), pedido.FechaEstimadaEntrega);
    }

    // ---------------------------------------------------------------------
    // Complementaria: un evento que no corresponde al paso pendiente se ignora.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task ManejarAsync_EventoFueraDeOrden_SeIgnoraSinPublicarNada()
    {
        // Arrange: la saga espera la respuesta de Inventario
        var escenario = new EscenarioSaga();
        await escenario.ConfirmarAsync();

        // Act: llega un PagoAprobado antes de tiempo
        var resultado = await escenario.ResponderAsync(EscenarioSaga.PagoAprobado());

        // Assert
        Assert.Equal(TipoResultadoSaga.EventoInesperado, resultado.Tipo);
        Assert.Equal(new[] { nameof(ReservarStock) }, escenario.Publisher.Tipos);
        Assert.Empty(escenario.SagaRepository.Guardados);
    }
}
