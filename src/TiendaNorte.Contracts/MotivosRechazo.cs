namespace TiendaNorte.Contracts;

/// <summary>
/// Códigos de motivo de rechazo. El enunciado exige respetarlos tal cual.
/// </summary>
public static class MotivosRechazo
{
    // Inventario
    public const string StockInsuficiente = "STOCK_INSUFICIENTE";
    public const string ProductoInexistente = "PRODUCTO_INEXISTENTE";

    // Pagos
    public const string DatosInvalidos = "DATOS_INVALIDOS";
    public const string TarjetaRechazada = "TARJETA_RECHAZADA";
    public const string MontoExcedido = "MONTO_EXCEDIDO";

    // Logística
    public const string SinCupo = "SIN_CUPO";
    public const string DireccionInvalida = "DIRECCION_INVALIDA";
    public const string FechaInvalida = "FECHA_INVALIDA";
}
