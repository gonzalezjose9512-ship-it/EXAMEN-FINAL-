namespace Pagos.Service.Mensajeria;

/// <summary>Conexión a RabbitMQ (sección "RabbitMQ" de appsettings.json o variables RabbitMQ__*).</summary>
public sealed class OpcionesRabbitMq
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Usuario { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
}
