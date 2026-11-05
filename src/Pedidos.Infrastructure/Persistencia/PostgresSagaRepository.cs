using System.Text.Json;
using Dapper;
using Npgsql;
using Pedidos.Application.Puertos;
using Pedidos.Application.Sagas;

namespace Pedidos.Infrastructure.Persistencia;

/// <summary>
/// Adaptador de ISagaRepository con PostgreSQL (tablas pedidos.sagas, pedidos.saga_pasos
/// y pedidos.mensajes_procesados), usando Npgsql + Dapper.
/// Si hay una transacción abierta en el PostgresUnitOfWork, participa de ella.
/// </summary>
public sealed class PostgresSagaRepository(PostgresUnitOfWork unitOfWork) : ISagaRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SagaConfirmacion?> ObtenerAsync(Guid sagaId, CancellationToken ct = default)
    {
        const string sqlSaga = """
            SELECT saga_id AS SagaId, pedido_id AS PedidoId, estado AS Estado, paso_actual AS PasoActual,
                   datos::text AS Datos, error AS Error,
                   fecha_creacion AS FechaCreacion, fecha_actualizacion AS FechaActualizacion
            FROM pedidos.sagas
            WHERE saga_id = @sagaId
            """;

        const string sqlPasos = """
            SELECT id AS Id, nombre AS Nombre, estado AS Estado, motivo AS Motivo,
                   resultado::text AS Resultado, fecha_utc AS FechaUtc
            FROM pedidos.saga_pasos
            WHERE saga_id = @sagaId
            ORDER BY id
            """;

        var conexion = await unitOfWork.ObtenerConexionAsync(ct);

        var fila = await conexion.QuerySingleOrDefaultAsync<FilaSaga>(
            new CommandDefinition(sqlSaga, new { sagaId }, unitOfWork.Transaccion, cancellationToken: ct));
        if (fila is null)
            return null;

        var pasos = await conexion.QueryAsync<FilaPaso>(
            new CommandDefinition(sqlPasos, new { sagaId }, unitOfWork.Transaccion, cancellationToken: ct));

        return new SagaConfirmacion
        {
            SagaId = fila.SagaId,
            PedidoId = fila.PedidoId,
            Estado = fila.Estado,
            PasoActual = fila.PasoActual,
            Datos = JsonSerializer.Deserialize<DatosSaga>(fila.Datos, Json)
                    ?? throw new InvalidOperationException($"La saga {sagaId} no tiene datos."),
            Error = fila.Error,
            FechaCreacion = fila.FechaCreacion,
            FechaActualizacion = fila.FechaActualizacion,
            Pasos = pasos.Select(p => new PasoSaga
            {
                Id = p.Id,
                Nombre = p.Nombre,
                Estado = p.Estado,
                Motivo = p.Motivo,
                Resultado = p.Resultado is null
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(p.Resultado, Json),
                FechaUtc = p.FechaUtc
            }).ToList()
        };
    }

    public async Task CrearAsync(SagaConfirmacion saga, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO pedidos.sagas
                (saga_id, pedido_id, estado, paso_actual, datos, error, fecha_creacion, fecha_actualizacion)
            VALUES
                (@SagaId, @PedidoId, @Estado, @PasoActual, CAST(@Datos AS jsonb), @Error, @FechaCreacion, @FechaActualizacion)
            """;

        var conexion = await unitOfWork.ObtenerConexionAsync(ct);
        var parametros = new
        {
            saga.SagaId,
            saga.PedidoId,
            saga.Estado,
            saga.PasoActual,
            Datos = JsonSerializer.Serialize(saga.Datos, Json),
            saga.Error,
            saga.FechaCreacion,
            saga.FechaActualizacion
        };
        await conexion.ExecuteAsync(new CommandDefinition(sql, parametros, unitOfWork.Transaccion, cancellationToken: ct));

        foreach (var paso in saga.Pasos)
            await InsertarPasoAsync(conexion, saga.SagaId, paso, ct);
    }

    public async Task ActualizarAsync(SagaConfirmacion saga, CancellationToken ct = default)
    {
        const string sqlSaga = """
            UPDATE pedidos.sagas
            SET estado = @Estado,
                paso_actual = @PasoActual,
                datos = CAST(@Datos AS jsonb),
                error = @Error,
                fecha_actualizacion = @FechaActualizacion
            WHERE saga_id = @SagaId
            """;

        const string sqlPaso = """
            UPDATE pedidos.saga_pasos
            SET estado = @Estado,
                motivo = @Motivo,
                resultado = CAST(@Resultado AS jsonb),
                fecha_utc = @FechaUtc
            WHERE id = @Id
            """;

        var conexion = await unitOfWork.ObtenerConexionAsync(ct);
        var parametros = new
        {
            saga.SagaId,
            saga.Estado,
            saga.PasoActual,
            Datos = JsonSerializer.Serialize(saga.Datos, Json),
            saga.Error,
            saga.FechaActualizacion
        };
        await conexion.ExecuteAsync(new CommandDefinition(sqlSaga, parametros, unitOfWork.Transaccion, cancellationToken: ct));

        foreach (var paso in saga.Pasos)
        {
            if (paso.Id == 0)
            {
                // Paso nuevo (por ejemplo, el siguiente que queda Pendiente)
                await InsertarPasoAsync(conexion, saga.SagaId, paso, ct);
            }
            else
            {
                var parametrosPaso = new
                {
                    paso.Id,
                    paso.Estado,
                    paso.Motivo,
                    Resultado = SerializarResultado(paso.Resultado),
                    paso.FechaUtc
                };
                await conexion.ExecuteAsync(new CommandDefinition(sqlPaso, parametrosPaso, unitOfWork.Transaccion, cancellationToken: ct));
            }
        }
    }

    public async Task<bool> MensajeYaProcesadoAsync(Guid messageId, CancellationToken ct = default)
    {
        const string sql = "SELECT EXISTS (SELECT 1 FROM pedidos.mensajes_procesados WHERE message_id = @messageId)";

        var conexion = await unitOfWork.ObtenerConexionAsync(ct);
        return await conexion.ExecuteScalarAsync<bool>(
            new CommandDefinition(sql, new { messageId }, unitOfWork.Transaccion, cancellationToken: ct));
    }

    public async Task RegistrarMensajeProcesadoAsync(Guid messageId, CancellationToken ct = default)
    {
        const string sql = "INSERT INTO pedidos.mensajes_procesados (message_id, fecha_utc) VALUES (@messageId, now())";

        var conexion = await unitOfWork.ObtenerConexionAsync(ct);
        await conexion.ExecuteAsync(
            new CommandDefinition(sql, new { messageId }, unitOfWork.Transaccion, cancellationToken: ct));
    }

    private async Task InsertarPasoAsync(NpgsqlConnection conexion, Guid sagaId, PasoSaga paso, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO pedidos.saga_pasos (saga_id, nombre, estado, motivo, resultado, fecha_utc)
            VALUES (@SagaId, @Nombre, @Estado, @Motivo, CAST(@Resultado AS jsonb), @FechaUtc)
            RETURNING id
            """;

        var parametros = new
        {
            SagaId = sagaId,
            paso.Nombre,
            paso.Estado,
            paso.Motivo,
            Resultado = SerializarResultado(paso.Resultado),
            paso.FechaUtc
        };

        // El id (bigserial) lo genera PostgreSQL: queda en el objeto para próximos UPDATE
        paso.Id = await conexion.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, parametros, unitOfWork.Transaccion, cancellationToken: ct));
    }

    private static string? SerializarResultado(Dictionary<string, string>? resultado) =>
        resultado is null ? null : JsonSerializer.Serialize(resultado, Json);

    private sealed class FilaSaga
    {
        public Guid SagaId { get; set; }
        public Guid PedidoId { get; set; }
        public string Estado { get; set; } = string.Empty;
        public string PasoActual { get; set; } = string.Empty;
        public string Datos { get; set; } = string.Empty;
        public string? Error { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime FechaActualizacion { get; set; }
    }

    private sealed class FilaPaso
    {
        public long Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public string? Motivo { get; set; }
        public string? Resultado { get; set; }
        public DateTime FechaUtc { get; set; }
    }
}
