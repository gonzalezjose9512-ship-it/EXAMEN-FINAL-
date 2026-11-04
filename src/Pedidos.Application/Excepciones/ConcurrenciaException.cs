namespace Pedidos.Application.Excepciones;

/// <summary>
/// Conflicto de versión al guardar eventos: otro proceso guardó un evento con la misma
/// versión antes (PostgreSQL rechazó la restricción UNIQUE (stream_id, version)).
/// La lanza el adaptador PostgresEventStore. La API la traduce a 409 Conflict.
/// </summary>
public sealed class ConcurrenciaException(string mensaje) : Exception(mensaje);
