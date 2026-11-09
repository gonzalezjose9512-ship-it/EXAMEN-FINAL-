using Inventario.Service.Abstracciones;
using TiendaNorte.Contracts;

namespace Inventario.Service.Handlers;

/// <summary>
/// Lógica del comando ReservarStock. Depende solo de interfaces (repositorio y publicador),
/// así las pruebas del grupo C la ejecutan con dobles, sin PostgreSQL ni RabbitMQ.
/// </summary>
public sealed class ReservarStockHandler(
    IInventarioRepository repositorio,
    IRespuestaPublisher publisher,
    TimeProvider tiempo,
    ILogger<ReservarStockHandler> logger)
{
    public async Task ManejarAsync(Mensaje<ReservarStock> comando, CancellationToken ct = default)
    {
        // Una línea por producto (si llegara repetido, se suman las cantidades)
        var items = comando.Datos.Items
            .GroupBy(i => i.ProductoId)
            .Select(g => new ItemStock(g.Key, g.Sum(i => i.Cantidad)))
            .ToList();

        StockRechazado? rechazo;
        string? reservaId = null;

        await repositorio.IniciarTransaccionAsync(ct);
        try
        {
            // Paso 3: idempotencia. Si ya se procesó, no se repite la acción
            if (await repositorio.MensajeYaProcesadoAsync(comando.MessageId, ct))
            {
                await repositorio.DeshacerTransaccionAsync(ct);
                logger.LogInformation("ReservarStock {MessageId} ya procesado: se ignora.", comando.MessageId);
                return;
            }

            // Paso 4: leer los productos bloqueándolos (SELECT ... FOR UPDATE)
            var stock = await repositorio.ObtenerStockBloqueandoAsync(items.Select(i => i.ProductoId).ToList(), ct);

            // Paso 5: revisar TODOS los ítems antes de modificar nada
            rechazo = BuscarRechazo(items, stock);

            // Paso 6: solo si no hubo rechazo, descontar stock y crear la reserva Activa
            if (rechazo is null)
                reservaId = await repositorio.CrearReservaAsync(comando.PedidoId, comando.SagaId, items, ct);

            // Paso 7: registrar el messageId y confirmar, en la misma transacción
            await repositorio.RegistrarMensajeProcesadoAsync(comando.MessageId, ct);
            await repositorio.ConfirmarTransaccionAsync(ct);
        }
        catch
        {
            await repositorio.DeshacerTransaccionAsync(CancellationToken.None);
            throw;
        }

        // Paso 8: publicar el resultado DESPUÉS del commit (copia sagaId y pedidoId)
        var ahora = tiempo.GetUtcNow().UtcDateTime;
        if (rechazo is null)
        {
            logger.LogInformation("Stock reservado: {ReservaId} para el pedido {PedidoId}", reservaId, comando.PedidoId);
            await publisher.PublicarAsync(Mensaje.ResponderA(comando, new StockReservado(reservaId!, items), ahora), ct);
        }
        else
        {
            logger.LogInformation("Stock rechazado ({Motivo}) para el pedido {PedidoId}", rechazo.Motivo, comando.PedidoId);
            await publisher.PublicarAsync(Mensaje.ResponderA(comando, rechazo, ahora), ct);
        }
    }

    /// <summary>
    /// Primero verifica que existan todos los productos y después que alcance el stock de cada uno.
    /// Devuelve null si se puede reservar todo.
    /// </summary>
    private static StockRechazado? BuscarRechazo(IReadOnlyList<ItemStock> items, IReadOnlyDictionary<string, int> stock)
    {
        foreach (var item in items)
        {
            if (!stock.ContainsKey(item.ProductoId))
                return new StockRechazado(MotivosRechazo.ProductoInexistente, item.ProductoId);
        }

        foreach (var item in items)
        {
            var disponible = stock[item.ProductoId];
            if (disponible < item.Cantidad)
                return new StockRechazado(MotivosRechazo.StockInsuficiente, item.ProductoId, item.Cantidad, disponible);
        }

        return null;
    }
}
