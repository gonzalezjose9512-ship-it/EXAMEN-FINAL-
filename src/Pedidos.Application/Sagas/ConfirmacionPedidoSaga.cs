using Pedidos.Application.CasosDeUso;
using Pedidos.Application.Puertos;
using Pedidos.Domain;
using TiendaNorte.Contracts;

namespace Pedidos.Application.Sagas;

/// <summary>
/// Saga ORQUESTADA de confirmación del pedido (Hito 3), implementada a mano: sin MassTransit ni NServiceBus.
///
/// Ante cada evento que responde un servicio, decide según la TABLA DE DECISIONES del enunciado:
///   1. cómo se actualiza la saga (estado, paso actual, datos, error);
///   2. qué paso se marca (Completado o Fallido) y qué paso nuevo queda Pendiente;
///   3. si corresponde un evento del pedido (PedidoConfirmado o PedidoRechazado);
///   4. qué comando se publica a continuación (o ninguno).
///
/// Camino feliz:  ReservarStock → Cobrar → ProgramarEnvio → pedido Confirmado.
/// Compensación EN ORDEN INVERSO de lo ya completado:
///   - falla Cobrar         → LiberarStock
///   - falla ProgramarEnvio → Reembolsar → LiberarStock
/// </summary>
public sealed class ConfirmacionPedidoSaga(
    ISagaRepository sagaRepository,
    IEventStore eventStore,
    IUnitOfWork unitOfWork,
    IMessagePublisher publisher,
    TimeProvider tiempo)
{
    /// <summary>
    /// Procesa un evento de pedidos.respuestas. La idempotencia por messageId la controla antes
    /// el consumidor; acá se registra el messageId dentro de la misma transacción.
    /// </summary>
    public async Task<ResultadoSaga> ManejarAsync<TEvento>(Mensaje<TEvento> evento, CancellationToken ct = default)
        where TEvento : notnull
    {
        // Paso 4: cargar la saga. Si no existe, el consumidor registra en el log y hace ack
        var saga = await sagaRepository.ObtenerAsync(evento.SagaId, ct);
        if (saga is null)
            return ResultadoSaga.SagaInexistente($"No existe la saga {evento.SagaId}.");

        // El paso que respondió tiene que estar Pendiente; si no, el evento llegó fuera de orden
        var nombrePaso = PasoQueResponde(evento.Datos);
        var paso = nombrePaso is null
            ? null
            : saga.Pasos.LastOrDefault(p => p.Nombre == nombrePaso && p.Estado == EstadosPaso.Pendiente);
        if (paso is null)
            return ResultadoSaga.EventoInesperado(
                $"La saga {saga.SagaId} no esperaba {evento.Tipo} (paso actual: {saga.PasoActual}, estado: {saga.Estado}).");

        var ahora = tiempo.GetUtcNow().UtcDateTime;
        Action<Pedido>? accionSobrePedido = null;
        Func<CancellationToken, Task>? publicarSiguiente = null;

        // Paso 5: tabla de decisiones
        switch (evento.Datos)
        {
            case StockReservado d:
                saga.Datos.ReservaId = d.ReservaId;
                Completar(paso, ahora, new() { ["reservaId"] = d.ReservaId });
                saga.PasoActual = PasosSaga.Cobrar;
                AgregarPasoPendiente(saga, PasosSaga.Cobrar, ahora);
                // Se cobra el total CON el descuento aplicado
                publicarSiguiente = Comando(saga, Topologia.ColaPagos, new Cobrar(saga.Datos.Total, saga.Datos.Tarjeta!), ahora);
                break;

            case StockRechazado d:
                // Falló el primer paso: no hay nada que compensar
                saga.Estado = EstadosSaga.Fallida;
                saga.Error = d.Motivo;
                Fallar(paso, ahora, d.Motivo);
                accionSobrePedido = pedido => pedido.Rechazar(d.Motivo);
                break;

            case PagoAprobado d:
                saga.Datos.Tarjeta = null; // la tarjeta se guarda solo hasta la respuesta de Pagos
                saga.Datos.PagoId = d.PagoId;
                saga.Datos.CodigoAutorizacion = d.CodigoAutorizacion;
                Completar(paso, ahora, new() { ["pagoId"] = d.PagoId });
                saga.PasoActual = PasosSaga.ProgramarEnvio;
                AgregarPasoPendiente(saga, PasosSaga.ProgramarEnvio, ahora);
                publicarSiguiente = Comando(saga, Topologia.ColaLogistica,
                    new ProgramarEnvio(saga.Datos.Direccion, saga.Datos.FechaEnvioPreferida), ahora);
                break;

            case PagoRechazado d:
                saga.Datos.Tarjeta = null;
                saga.Estado = EstadosSaga.Compensando;
                saga.Error = d.Motivo;
                Fallar(paso, ahora, d.Motivo, new() { ["pagoId"] = d.PagoId });
                // Compensación: deshacer lo único completado (la reserva de stock)
                saga.PasoActual = PasosSaga.LiberarStock;
                AgregarPasoPendiente(saga, PasosSaga.LiberarStock, ahora);
                publicarSiguiente = Comando(saga, Topologia.ColaInventario, new LiberarStock(saga.Datos.ReservaId!), ahora);
                break;

            case EnvioProgramado d:
                saga.Estado = EstadosSaga.Completada;
                Completar(paso, ahora, new()
                {
                    ["envioId"] = d.EnvioId,
                    ["codigoSeguimiento"] = d.CodigoSeguimiento
                });
                var codigoAutorizacion = saga.Datos.CodigoAutorizacion!;
                accionSobrePedido = pedido => pedido.Confirmar(codigoAutorizacion, d.CodigoSeguimiento, d.FechaEstimadaEntrega);
                break;

            case EnvioRechazado d:
                saga.Estado = EstadosSaga.Compensando;
                saga.Error = d.Motivo;
                Fallar(paso, ahora, d.Motivo);
                // Compensación en ORDEN INVERSO: primero el pago (lo último completado)...
                saga.PasoActual = PasosSaga.Reembolsar;
                AgregarPasoPendiente(saga, PasosSaga.Reembolsar, ahora);
                publicarSiguiente = Comando(saga, Topologia.ColaPagos, new Reembolsar(saga.Datos.PagoId!), ahora);
                break;

            case PagoReembolsado:
                Completar(paso, ahora);
                // ...y después el stock
                saga.PasoActual = PasosSaga.LiberarStock;
                AgregarPasoPendiente(saga, PasosSaga.LiberarStock, ahora);
                publicarSiguiente = Comando(saga, Topologia.ColaInventario, new LiberarStock(saga.Datos.ReservaId!), ahora);
                break;

            case StockLiberado:
                saga.Estado = EstadosSaga.Compensada;
                Completar(paso, ahora);
                var motivo = saga.Error!;
                accionSobrePedido = pedido => pedido.Rechazar(motivo);
                break;
        }

        saga.FechaActualizacion = ahora;

        // Paso 6: en UNA transacción, la saga, sus pasos, el evento del pedido (si hay) y el messageId
        await unitOfWork.IniciarAsync(ct);
        try
        {
            await sagaRepository.ActualizarAsync(saga, ct);

            if (accionSobrePedido is not null)
            {
                var pedido = await eventStore.CargarPedidoAsync(saga.PedidoId, ct);
                var versionLeida = pedido.Version;
                accionSobrePedido(pedido); // Confirmar o Rechazar: EstadoInvalidoException si no está PendienteConfirmacion
                await eventStore.GuardarAsync(saga.PedidoId, versionLeida, pedido.EventosNuevos, ct);
            }

            await sagaRepository.RegistrarMensajeProcesadoAsync(evento.MessageId, ct);
            await unitOfWork.ConfirmarAsync(ct);
        }
        catch
        {
            await unitOfWork.DeshacerAsync(CancellationToken.None);
            throw;
        }

        // Paso 7: DESPUÉS del commit, publicar el comando siguiente (si la tabla indica uno)
        if (publicarSiguiente is not null)
            await publicarSiguiente(ct);

        return ResultadoSaga.Procesado($"Saga {saga.SagaId}: {evento.Tipo} → estado {saga.Estado}, paso actual {saga.PasoActual}.");
    }

    /// <summary>Qué paso de la saga responde cada evento.</summary>
    private static string? PasoQueResponde(object datos) => datos switch
    {
        StockReservado or StockRechazado => PasosSaga.ReservarStock,
        PagoAprobado or PagoRechazado => PasosSaga.Cobrar,
        EnvioProgramado or EnvioRechazado => PasosSaga.ProgramarEnvio,
        PagoReembolsado => PasosSaga.Reembolsar,
        StockLiberado => PasosSaga.LiberarStock,
        _ => null // por ejemplo EnvioAnulado: no forma parte de la saga obligatoria
    };

    private static void Completar(PasoSaga paso, DateTime ahora, Dictionary<string, string>? resultado = null)
    {
        paso.Estado = EstadosPaso.Completado;
        paso.Resultado = resultado;
        paso.FechaUtc = ahora;
    }

    private static void Fallar(PasoSaga paso, DateTime ahora, string motivo, Dictionary<string, string>? resultado = null)
    {
        paso.Estado = EstadosPaso.Fallido;
        paso.Motivo = motivo;
        paso.Resultado = resultado;
        paso.FechaUtc = ahora;
    }

    private static void AgregarPasoPendiente(SagaConfirmacion saga, string nombre, DateTime ahora) =>
        saga.Pasos.Add(new PasoSaga { Nombre = nombre, Estado = EstadosPaso.Pendiente, FechaUtc = ahora });

    /// <summary>Prepara la publicación del comando siguiente; se ejecuta recién después del commit.</summary>
    private Func<CancellationToken, Task> Comando<TComando>(SagaConfirmacion saga, string cola, TComando datos, DateTime ahora)
        where TComando : notnull =>
        ct => publisher.PublicarAsync(cola, Mensaje.Crear(saga.SagaId, saga.PedidoId, datos, ahora), ct);
}

/// <summary>Resultado de procesar un evento, para que el consumidor lo registre en el log.</summary>
public sealed record ResultadoSaga(TipoResultadoSaga Tipo, string Detalle)
{
    public static ResultadoSaga Procesado(string detalle) => new(TipoResultadoSaga.Procesado, detalle);
    public static ResultadoSaga SagaInexistente(string detalle) => new(TipoResultadoSaga.SagaInexistente, detalle);
    public static ResultadoSaga EventoInesperado(string detalle) => new(TipoResultadoSaga.EventoInesperado, detalle);
}

public enum TipoResultadoSaga
{
    Procesado,
    SagaInexistente,
    EventoInesperado
}
