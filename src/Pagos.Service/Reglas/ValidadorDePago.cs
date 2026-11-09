using System.Globalization;
using TiendaNorte.Contracts;

namespace Pagos.Service.Reglas;

/// <summary>
/// Reglas de rechazo de la pasarela simulada. Se aplican EN ESTE ORDEN y se devuelve
/// el primer motivo que corresponda:
///   1. monto ≤ 0, número sin 16 dígitos, o vencimiento inválido o pasado → DATOS_INVALIDOS
///   2. número terminado en 0000                                           → TARJETA_RECHAZADA
///   3. monto mayor a $500.000                                              → MONTO_EXCEDIDO
/// Recibe TimeProvider para saber la fecha actual: las pruebas pueden fijarla con FakeTimeProvider.
/// </summary>
public sealed class ValidadorDePago(TimeProvider tiempo)
{
    public const decimal MontoMaximo = 500_000m;

    /// <summary>Null si el pago se puede aprobar; si no, el motivo del rechazo.</summary>
    public RechazoDePago? Validar(decimal monto, DatosTarjeta tarjeta)
    {
        // (1) Datos inválidos
        if (monto <= 0)
            return new RechazoDePago(MotivosRechazo.DatosInvalidos, "El monto debe ser mayor a 0");

        var numero = tarjeta.Numero ?? string.Empty;
        if (numero.Length != 16 || !numero.All(char.IsAsciiDigit))
            return new RechazoDePago(MotivosRechazo.DatosInvalidos, "El número de tarjeta debe tener 16 dígitos");

        if (!TryLeerVencimiento(tarjeta.Vencimiento, out var anio, out var mes))
            return new RechazoDePago(MotivosRechazo.DatosInvalidos, "Vencimiento inválido: se espera MM/AA");

        // La tarjeta vale hasta el último día de su mes de vencimiento
        var hoy = tiempo.GetLocalNow();
        if (anio < hoy.Year || (anio == hoy.Year && mes < hoy.Month))
            return new RechazoDePago(MotivosRechazo.DatosInvalidos, "Tarjeta vencida");

        // (2) Tarjeta rechazada por el emisor (simulado)
        if (numero.EndsWith("0000", StringComparison.Ordinal))
            return new RechazoDePago(MotivosRechazo.TarjetaRechazada);

        // (3) Monto excedido
        if (monto > MontoMaximo)
            return new RechazoDePago(MotivosRechazo.MontoExcedido, Limite: MontoMaximo);

        return null;
    }

    /// <summary>Lee "MM/AA" (por ejemplo 12/28 = diciembre de 2028).</summary>
    private static bool TryLeerVencimiento(string? texto, out int anio, out int mes)
    {
        anio = 0;
        mes = 0;
        if (texto is null || texto.Length != 5 || texto[2] != '/')
            return false;

        if (!int.TryParse(texto.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out mes) ||
            !int.TryParse(texto.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var anioCorto) ||
            mes is < 1 or > 12)
            return false;

        anio = 2000 + anioCorto;
        return true;
    }
}

/// <summary>Motivo del rechazo y datos extra para el evento PagoRechazado.</summary>
public sealed record RechazoDePago(string Motivo, string? Detalle = null, decimal? Limite = null);
