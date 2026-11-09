using System.ComponentModel.DataAnnotations;
using System.Globalization;

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

/// <summary>
/// Body de POST /api/pedidos/{pedidoId}/confirmar.
/// Solo se valida que vengan los datos; si la tarjeta es válida lo decide Pagos, no esta API.
/// </summary>
public sealed class ConfirmarPedidoRequest : IValidatableObject
{
    [Required]
    public TarjetaRequest? Tarjeta { get; init; }

    /// <summary>Opcional. Formato AAAA-MM-DD y posterior a hoy.</summary>
    public string? FechaEnvioPreferida { get; init; }

    /// <summary>La fecha ya validada como DateOnly, o null si no vino.</summary>
    public DateOnly? ObtenerFechaEnvioPreferida() =>
        FechaEnvioPreferida is null ? null : ParsearFecha(FechaEnvioPreferida);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FechaEnvioPreferida is null)
            yield break;

        var fecha = ParsearFecha(FechaEnvioPreferida);
        if (fecha is null)
        {
            yield return new ValidationResult(
                "La fecha de envío preferida debe tener formato AAAA-MM-DD.", [nameof(FechaEnvioPreferida)]);
            yield break;
        }

        // "Hoy" sale de TimeProvider (no de DateTime.Now) para que las pruebas puedan fijar la fecha
        var tiempo = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        var hoy = DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);
        if (fecha <= hoy)
            yield return new ValidationResult(
                "La fecha de envío preferida debe ser posterior a hoy.", [nameof(FechaEnvioPreferida)]);
    }

    private static DateOnly? ParsearFecha(string texto) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
            ? fecha
            : null;
}

/// <summary>Los 3 campos de la tarjeta son obligatorios.</summary>
public sealed class TarjetaRequest
{
    [Required]
    public string? Numero { get; init; }

    [Required]
    public string? Titular { get; init; }

    [Required]
    public string? Vencimiento { get; init; }
}
