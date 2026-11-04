using TiendaNorte.Contracts;

namespace Pedidos.Application.Sagas;

/// <summary>
/// Estado de una saga de confirmación (una fila de pedidos.sagas más sus pasos de pedidos.saga_pasos).
/// La decisión de qué hacer ante cada evento está en ConfirmacionPedidoSaga.
/// </summary>
public sealed class SagaConfirmacion
{
    public required Guid SagaId { get; init; }
    public required Guid PedidoId { get; init; }

    /// <summary>EnCurso, Completada, Compensando, Compensada o Fallida (ver <see cref="EstadosSaga"/>).</summary>
    public required string Estado { get; set; }

    /// <summary>Nombre del paso que se está esperando (ReservarStock, Cobrar, ProgramarEnvio...).</summary>
    public required string PasoActual { get; set; }

    /// <summary>Datos que la saga necesita para los próximos pasos (columna datos jsonb).</summary>
    public required DatosSaga Datos { get; set; }

    /// <summary>Motivo del primer paso que falló (por ejemplo SIN_CUPO). Null si no falló nada.</summary>
    public string? Error { get; set; }

    public required DateTime FechaCreacion { get; init; }
    public required DateTime FechaActualizacion { get; set; }

    /// <summary>Historial de pasos y compensaciones, en orden de ejecución.</summary>
    public List<PasoSaga> Pasos { get; init; } = [];
}

/// <summary>
/// Contenido de la columna pedidos.sagas.datos.
/// La tarjeta se guarda solo hasta recibir la respuesta de Pagos; después se borra (queda en null).
/// </summary>
public sealed class DatosSaga
{
    public DatosTarjeta? Tarjeta { get; set; }
    public List<ItemStock> Items { get; set; } = [];

    /// <summary>Total del pedido con el descuento ya aplicado (es el monto que se cobra).</summary>
    public decimal Total { get; set; }

    public required DireccionEnvio Direccion { get; set; }
    public DateOnly? FechaEnvioPreferida { get; set; }

    // Ids que devuelven los servicios y se necesitan para compensar
    public string? ReservaId { get; set; }
    public string? PagoId { get; set; }
    public string? CodigoAutorizacion { get; set; }
}

/// <summary>Una fila de pedidos.saga_pasos.</summary>
public sealed class PasoSaga
{
    /// <summary>Id de la fila (bigserial). 0 = paso nuevo que todavía no se guardó.</summary>
    public long Id { get; set; }

    public required string Nombre { get; init; }

    /// <summary>Pendiente, Completado o Fallido (ver <see cref="EstadosPaso"/>).</summary>
    public required string Estado { get; set; }

    /// <summary>Motivo del rechazo cuando el paso falla (por ejemplo TARJETA_RECHAZADA).</summary>
    public string? Motivo { get; set; }

    /// <summary>Resultado del paso, por ejemplo { "reservaId": "R-000015" }.</summary>
    public Dictionary<string, string>? Resultado { get; set; }

    public required DateTime FechaUtc { get; set; }
}

/// <summary>Valores posibles del estado de la saga.</summary>
public static class EstadosSaga
{
    public const string EnCurso = "EnCurso";
    public const string Completada = "Completada";
    public const string Compensando = "Compensando";
    public const string Compensada = "Compensada";

    /// <summary>Falló el primer paso y no hubo nada que compensar.</summary>
    public const string Fallida = "Fallida";
}

/// <summary>Valores posibles del estado de un paso.</summary>
public static class EstadosPaso
{
    public const string Pendiente = "Pendiente";
    public const string Completado = "Completado";
    public const string Fallido = "Fallido";
}

/// <summary>Nombres de los pasos: coinciden con el nombre del comando que se publica.</summary>
public static class PasosSaga
{
    public const string ReservarStock = nameof(TiendaNorte.Contracts.ReservarStock);
    public const string Cobrar = nameof(TiendaNorte.Contracts.Cobrar);
    public const string ProgramarEnvio = nameof(TiendaNorte.Contracts.ProgramarEnvio);
    public const string LiberarStock = nameof(TiendaNorte.Contracts.LiberarStock);
    public const string Reembolsar = nameof(TiendaNorte.Contracts.Reembolsar);
    public const string AnularEnvio = nameof(TiendaNorte.Contracts.AnularEnvio);
}
