namespace Logistica.Service.Reglas;

/// <summary>
/// Configuración de Logística leída con IOptions desde appsettings.json.
/// CupoDiario = máximo de envíos Programado por fecha de despacho (por defecto 3).
/// Para el escenario E4 se pone en 0.
/// </summary>
public sealed class OpcionesLogistica
{
    public int CupoDiario { get; set; } = 3;
}
