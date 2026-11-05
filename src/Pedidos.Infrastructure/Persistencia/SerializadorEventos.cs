using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pedidos.Domain.Eventos;

namespace Pedidos.Infrastructure.Persistencia;

/// <summary>
/// Convierte los eventos del pedido a JSON (columna datos) y de vuelta al record que indica tipo_evento.
///
/// PedidoId, Version y FechaUtc son metadatos: van en las columnas stream_id, version y fecha_utc,
/// por eso se quitan del JSON. Así datos queda igual que el ejemplo del endpoint 5:
/// { "productoId": "P001", "cantidad": 2, "precioUnitario": 45000 }.
/// </summary>
internal static class SerializadorEventos
{
    /// <summary>tipo_evento → tipo de C#. Un evento nuevo del dominio se agrega acá.</summary>
    private static readonly Dictionary<string, Type> Tipos = new[]
    {
        typeof(PedidoCreado),
        typeof(ItemAgregado),
        typeof(ConfirmacionSolicitada),
        typeof(PedidoConfirmado),
        typeof(PedidoRechazado)
    }.ToDictionary(t => t.Name);

    private static readonly string[] Metadatos = ["pedidoId", "version", "fechaUtc"];

    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { QuitarMetadatos } }
    };

    /// <summary>Valor de la columna tipo_evento: el nombre del record (PedidoCreado, ItemAgregado...).</summary>
    public static string NombreTipo(EventoPedido evento) => evento.GetType().Name;

    /// <summary>Contenido del evento en JSON, sin los metadatos.</summary>
    public static string Serializar(EventoPedido evento) =>
        JsonSerializer.Serialize(evento, evento.GetType(), Opciones);

    /// <summary>Reconstruye el record a partir de una fila de pedidos.eventos.</summary>
    public static EventoPedido Deserializar(string tipoEvento, string datosJson, Guid pedidoId, int version, DateTime fechaUtc)
    {
        if (!Tipos.TryGetValue(tipoEvento, out var tipo))
            throw new InvalidOperationException($"Tipo de evento desconocido en el event store: {tipoEvento}.");

        var evento = (EventoPedido?)JsonSerializer.Deserialize(datosJson, tipo, Opciones)
            ?? throw new InvalidOperationException($"No se pudo leer el evento {tipoEvento} versión {version}.");

        // Los metadatos salen de las columnas de la fila
        return evento with { PedidoId = pedidoId, Version = version, FechaUtc = fechaUtc };
    }

    /// <summary>Quita PedidoId, Version y FechaUtc del JSON de cualquier evento del pedido.</summary>
    private static void QuitarMetadatos(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || !typeof(EventoPedido).IsAssignableFrom(info.Type))
            return;

        for (var i = info.Properties.Count - 1; i >= 0; i--)
        {
            if (Metadatos.Contains(info.Properties[i].Name))
                info.Properties.RemoveAt(i);
        }
    }
}
