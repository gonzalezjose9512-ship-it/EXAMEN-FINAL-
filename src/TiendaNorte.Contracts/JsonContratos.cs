using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiendaNorte.Contracts;

/// <summary>
/// Opciones de JSON compartidas por los 4 componentes:
/// camelCase (messageId, sagaId, pedidoId...), lectura sin distinguir mayúsculas
/// y sin escribir las propiedades que son null.
/// </summary>
public static class JsonContratos
{
    public static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
