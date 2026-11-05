using Dapper;
using Npgsql;
using Pedidos.Application.Excepciones;
using Pedidos.Application.Puertos;
using Pedidos.Domain.Eventos;

namespace Pedidos.Infrastructure.Persistencia;

/// <summary>
/// Adaptador de IEventStore con PostgreSQL (tabla pedidos.eventos), usando Npgsql + Dapper.
/// Si hay una transacción abierta en el PostgresUnitOfWork, participa de ella.
/// </summary>
public sealed class PostgresEventStore(PostgresUnitOfWork unitOfWork) : IEventStore
{
    public async Task<IReadOnlyList<EventoPedido>> LeerAsync(Guid pedidoId, CancellationToken ct = default)
    {
        var filas = await LeerFilasInternasAsync(pedidoId, ct);

        // Cada fila se convierte al record que indica tipo_evento
        return filas
            .Select(f => SerializadorEventos.Deserializar(f.TipoEvento, f.Datos, pedidoId, f.Version, f.FechaUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<EventoGuardado>> LeerFilasAsync(Guid pedidoId, CancellationToken ct = default)
    {
        var filas = await LeerFilasInternasAsync(pedidoId, ct);

        // Sin rehidratar: cada fila tal como está guardada (endpoint 5)
        return filas
            .Select(f => new EventoGuardado(f.Version, f.TipoEvento, f.FechaUtc, f.Datos))
            .ToList();
    }

    public async Task GuardarAsync(Guid pedidoId, int versionEsperada, IReadOnlyList<EventoPedido> eventosNuevos, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO pedidos.eventos (stream_id, version, tipo_evento, datos, fecha_utc)
            VALUES (@StreamId, @Version, @TipoEvento, CAST(@Datos AS jsonb), @FechaUtc)
            """;

        var conexion = await unitOfWork.ObtenerConexionAsync(ct);
        var version = versionEsperada;

        try
        {
            foreach (var evento in eventosNuevos)
            {
                version++; // versión leída + 1, + 2...
                var parametros = new
                {
                    StreamId = pedidoId,
                    Version = version,
                    TipoEvento = SerializadorEventos.NombreTipo(evento),
                    Datos = SerializadorEventos.Serializar(evento),
                    evento.FechaUtc
                };
                await conexion.ExecuteAsync(new CommandDefinition(sql, parametros, unitOfWork.Transaccion, cancellationToken: ct));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // unique_violation de UNIQUE (stream_id, version)
        {
            throw new ConcurrenciaException(
                $"El pedido {pedidoId} fue modificado por otra operación: la versión {version} ya existe. Consultalo de nuevo y reintentá.");
        }
    }

    private async Task<IReadOnlyList<FilaEvento>> LeerFilasInternasAsync(Guid pedidoId, CancellationToken ct)
    {
        const string sql = """
            SELECT version, tipo_evento AS TipoEvento, fecha_utc AS FechaUtc, datos::text AS Datos
            FROM pedidos.eventos
            WHERE stream_id = @pedidoId
            ORDER BY version
            """;

        var conexion = await unitOfWork.ObtenerConexionAsync(ct);
        var filas = await conexion.QueryAsync<FilaEvento>(
            new CommandDefinition(sql, new { pedidoId }, unitOfWork.Transaccion, cancellationToken: ct));
        return filas.ToList();
    }

    /// <summary>Una fila de pedidos.eventos tal como la devuelve Dapper.</summary>
    private sealed class FilaEvento
    {
        public int Version { get; set; }
        public string TipoEvento { get; set; } = string.Empty;
        public DateTime FechaUtc { get; set; }
        public string Datos { get; set; } = string.Empty;
    }
}
