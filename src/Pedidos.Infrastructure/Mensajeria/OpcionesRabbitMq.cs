namespace Pedidos.Infrastructure.Mensajeria;

/// <summary>
/// Conexión a RabbitMQ, leída de la sección "RabbitMQ" de appsettings.json
/// (o de variables de entorno RabbitMQ__Host, RabbitMQ__Port... en Docker).
/// </summary>
public sealed class OpcionesRabbitMq
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Usuario { get; set; } = string.Empty;
    public string Clave { get; set; } = string.Empty;
}
