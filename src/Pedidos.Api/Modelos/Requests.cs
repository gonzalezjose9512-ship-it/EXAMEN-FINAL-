using System.ComponentModel.DataAnnotations;

namespace Pedidos.Api.Modelos;

// Validación de FORMATO con atributos: si falla, [ApiController] responde 400 automáticamente
// con el detalle por campo en "errors" (ValidationProblemDetails).
// Las reglas de NEGOCIO no van acá: las valida el dominio (422 / 409).

/// <summary>Body de POST /api/pedidos.</summary>
public sealed class CrearPedidoRequest
{
    [Required]
    public string? ClienteId { get; init; }

    [Required]
    public DireccionRequest? DireccionEnvio { get; init; }
}

/// <summary>Los 4 campos de la dirección son obligatorios.</summary>
public sealed class DireccionRequest
{
    [Required]
    public string? Calle { get; init; }

    [Required]
    public string? Numero { get; init; }

    [Required]
    public string? Ciudad { get; init; }

    [Required]
    public string? CodigoPostal { get; init; }
}

/// <summary>Body de POST /api/pedidos/{pedidoId}/items.</summary>
public sealed class AgregarItemRequest
{
    [Required]
    public string? ProductoId { get; init; }

    /// <summary>Entre 1 y 10.</summary>
    [Range(1, 10)]
    public int Cantidad { get; init; }

    /// <summary>Mayor a 0.</summary>
    [Range(0d, double.MaxValue, MinimumIsExclusive = true)]
    public decimal PrecioUnitario { get; init; }
}
