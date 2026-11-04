namespace TiendaNorte.Contracts;

/// <summary>
/// Sobre común de TODOS los mensajes (comandos y eventos) que viajan por RabbitMQ.
/// Serializado en JSON queda en camelCase:
/// { "messageId", "sagaId", "pedidoId", "tipo", "fechaUtc", "datos" }.
/// El contenido propio de cada mensaje va en <see cref="Datos"/>.
/// </summary>
/// <typeparam name="TDatos">Record con los datos del comando o del evento (por ejemplo, <see cref="ReservarStock"/>).</typeparam>
public sealed record Mensaje<TDatos>(
    Guid MessageId,
    Guid SagaId,
    Guid PedidoId,
    string Tipo,
    DateTime FechaUtc,
    TDatos Datos);

/// <summary>
/// Ayudas para armar sobres de mensajes.
/// </summary>
public static class Mensaje
{
    /// <summary>
    /// Crea un mensaje nuevo con un MessageId nuevo.
    /// El "tipo" es el nombre del record de datos (ReservarStock, StockReservado, etc.).
    /// </summary>
    public static Mensaje<TDatos> Crear<TDatos>(Guid sagaId, Guid pedidoId, TDatos datos, DateTime fechaUtc)
        where TDatos : notnull
        => new(Guid.NewGuid(), sagaId, pedidoId, typeof(TDatos).Name, fechaUtc, datos);

    /// <summary>
    /// Crea el evento de respuesta a un comando: copia SagaId y PedidoId del comando
    /// y genera un MessageId nuevo (regla de correlación del enunciado).
    /// </summary>
    public static Mensaje<TRespuesta> ResponderA<TComando, TRespuesta>(
        Mensaje<TComando> comando, TRespuesta datos, DateTime fechaUtc)
        where TRespuesta : notnull
        => Crear(comando.SagaId, comando.PedidoId, datos, fechaUtc);
}
