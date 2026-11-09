using Pedidos.Application.Puertos;
using Pedidos.Application.Sagas;
using Pedidos.Domain;
using TiendaNorte.Contracts;

namespace Pedidos.Application.CasosDeUso;

/// <summary>
/// Endpoint 3 - POST /api/pedidos/{pedidoId}/confirmar.
/// Inicia la saga de confirmación y responde SIN esperar el resultado
/// (llega después, por los eventos de los servicios).
/// </summary>
public sealed class ConfirmarPedido(
    IEventStore eventStore,
    ISagaRepository sagaRepository,
    IUnitOfWork unitOfWork,
    IMessagePublisher publisher,
    TimeProvider tiempo)
{
    public async Task<ResultadoConfirmacion> EjecutarAsync(
        Guid pedidoId, DatosTarjeta tarjeta, DateOnly? fechaEnvioPreferida, CancellationToken ct = default)
    {
        // Pasos 2 y 3: leer y rehidratar (404 si no existe) y generar el sagaId
        var pedido = await eventStore.CargarPedidoAsync(pedidoId, ct);
        var versionLeida = pedido.Version;
        var sagaId = Guid.NewGuid();

        // Paso 4: el dominio valida (409 si no está en Borrador, 422 si no tiene ítems)
        // y genera ConfirmacionSolicitada; el pedido pasa a PendienteConfirmacion
        pedido.SolicitarConfirmacion(sagaId);

        // Paso 5: armar la saga. Inventario recibe una línea por producto (suma de cantidades)
        var ahora = tiempo.GetUtcNow().UtcDateTime;
        var items = pedido.Items
            .GroupBy(i => i.ProductoId)
            .Select(g => new ItemStock(g.Key, g.Sum(i => i.Cantidad)))
            .ToList();

        var saga = new SagaConfirmacion
        {
            SagaId = sagaId,
            PedidoId = pedidoId,
            Estado = EstadosSaga.EnCurso,
            PasoActual = PasosSaga.ReservarStock,
            Datos = new DatosSaga
            {
                Tarjeta = tarjeta, // se borra al recibir la respuesta de Pagos
                Items = items,
                Total = pedido.Total, // con el descuento aplicado: es lo que se cobra
                Direccion = new DireccionEnvio(
                    pedido.DireccionEnvio.Calle,
                    pedido.DireccionEnvio.Numero,
                    pedido.DireccionEnvio.Ciudad,
                    pedido.DireccionEnvio.CodigoPostal),
                FechaEnvioPreferida = fechaEnvioPreferida
            },
            FechaCreacion = ahora,
            FechaActualizacion = ahora,
            Pasos =
            [
                new PasoSaga { Nombre = PasosSaga.ReservarStock, Estado = EstadosPaso.Pendiente, FechaUtc = ahora }
            ]
        };

        // Paso 6: en UNA sola transacción, el evento, la saga y su primer paso
        await unitOfWork.IniciarAsync(ct);
        try
        {
            await eventStore.GuardarAsync(pedidoId, versionLeida, pedido.EventosNuevos, ct);
            await sagaRepository.CrearAsync(saga, ct);
            await unitOfWork.ConfirmarAsync(ct);
        }
        catch
        {
            await unitOfWork.DeshacerAsync(CancellationToken.None);
            throw;
        }

        // Paso 7: DESPUÉS del commit, publicar ReservarStock en inventario.comandos.
        // Si esto falla, la saga queda EnCurso sin mensaje (aceptado en el examen; la solución completa es Outbox).
        var comando = Mensaje.Crear(sagaId, pedidoId, new ReservarStock(items), ahora);
        await publisher.PublicarAsync(Topologia.ColaInventario, comando, ct);

        return new ResultadoConfirmacion(pedidoId, sagaId, pedido.Estado.ToString());
    }
}

/// <summary>Lo que devuelve ConfirmarPedido para armar la respuesta 202.</summary>
public sealed record ResultadoConfirmacion(Guid PedidoId, Guid SagaId, string Estado);
