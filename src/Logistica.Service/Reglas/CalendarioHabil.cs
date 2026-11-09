namespace Logistica.Service.Reglas;

/// <summary>
/// Cálculo de días hábiles (lunes a viernes). Recibe TimeProvider para saber qué día es hoy:
/// las pruebas pueden fijar la fecha con FakeTimeProvider.
/// </summary>
public sealed class CalendarioHabil(TimeProvider tiempo)
{
    public const int DiasHabilesDeEntrega = 3;

    public static bool EsFinDeSemana(DateOnly fecha) =>
        fecha.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>
    /// Fecha de despacho: la preferida si viene; si no, el próximo día hábil desde mañana.
    /// (Que la preferida no caiga en fin de semana lo valida el handler antes: FECHA_INVALIDA.)
    /// </summary>
    public DateOnly CalcularFechaDespacho(DateOnly? fechaPreferida)
    {
        if (fechaPreferida is { } preferida)
            return preferida;

        var hoy = DateOnly.FromDateTime(tiempo.GetLocalNow().DateTime);
        var fecha = hoy.AddDays(1);
        while (EsFinDeSemana(fecha))
            fecha = fecha.AddDays(1);
        return fecha;
    }

    /// <summary>Entrega estimada = despacho + 3 días hábiles (salta sábados y domingos).</summary>
    public static DateOnly CalcularEntregaEstimada(DateOnly fechaDespacho)
    {
        var fecha = fechaDespacho;
        var sumados = 0;
        while (sumados < DiasHabilesDeEntrega)
        {
            fecha = fecha.AddDays(1);
            if (!EsFinDeSemana(fecha))
                sumados++;
        }
        return fecha;
    }
}
