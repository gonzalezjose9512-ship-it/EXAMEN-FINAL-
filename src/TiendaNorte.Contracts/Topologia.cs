namespace TiendaNorte.Contracts;

/// <summary>
/// Nombres del exchange y de las colas de RabbitMQ (Hito 6).
/// La routing key de cada cola es igual al nombre de la cola.
/// </summary>
public static class Topologia
{
    /// <summary>Exchange tipo direct y durable.</summary>
    public const string Exchange = "tiendanorte";

    public const string ColaInventario = "inventario.comandos";
    public const string ColaPagos = "pagos.comandos";
    public const string ColaLogistica = "logistica.comandos";

    /// <summary>Todos los eventos de los 3 servicios van a esta cola.</summary>
    public const string ColaRespuestas = "pedidos.respuestas";

    /// <summary>
    /// Las 4 colas. Cada componente las declara al arrancar para que un mensaje
    /// nunca se pierda aunque el servicio destino todavía no se haya levantado (escenario E5).
    /// </summary>
    public static readonly IReadOnlyList<string> TodasLasColas =
        [ColaInventario, ColaPagos, ColaLogistica, ColaRespuestas];
}
