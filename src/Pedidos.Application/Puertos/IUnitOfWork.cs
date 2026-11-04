namespace Pedidos.Application.Puertos;

/// <summary>
/// Puerto de salida: transacción única para varias operaciones de persistencia.
/// Lo implementa PostgresUnitOfWork con una NpgsqlTransaction que comparten
/// PostgresEventStore y PostgresSagaRepository.
/// Uso: IniciarAsync → guardar evento, saga y pasos → ConfirmarAsync (o DeshacerAsync si algo falla).
/// </summary>
public interface IUnitOfWork
{
    Task IniciarAsync(CancellationToken ct = default);
    Task ConfirmarAsync(CancellationToken ct = default);
    Task DeshacerAsync(CancellationToken ct = default);
}
