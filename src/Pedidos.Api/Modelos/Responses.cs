using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pedidos.Api.Modelos;

/// <summary>Respuesta 201 de POST /api/pedidos.</summary>
public sealed record CrearPedidoResponse(Guid PedidoId, string Estado, int Version);

/// <summary>Respuesta 202 de POST /api/pedidos/{pedidoId}/confirmar.</summary>
public sealed record ConfirmarPedidoResponse(Guid PedidoId, Guid SagaId, string Estado);

/// <summary>Estado del pedido (endpoints 2 y 4).</summary>
public sealed record PedidoResponse(
    Guid PedidoId,
    string ClienteId,
    string Estado,
    DireccionResponse DireccionEnvio,
    IReadOnlyList<ItemResponse> Items,
    decimal Subtotal,
    decimal Descuento,
    decimal Total,
    int Version,
    ConfirmacionResponse? Confirmacion,  // solo si está Confirmado
    string? MotivoRechazo);              // solo si está Rechazado

public sealed record DireccionResponse(string Calle, string Numero, string Ciudad, string CodigoPostal);

public sealed record ItemResponse(string ProductoId, int Cantidad, decimal PrecioUnitario, decimal Subtotal);

public sealed record ConfirmacionResponse(string CodigoAutorizacion, string CodigoSeguimiento, DateOnly FechaEstimadaEntrega);

/// <summary>
/// Un evento tal como está guardado (endpoint 5).
/// Datos es JsonElement para que salga como objeto JSON y no como texto.
/// </summary>
public sealed record EventoResponse(int Version, string Tipo, DateTime FechaUtc, JsonElement Datos);

/// <summary>Estado de la saga y sus pasos (endpoint 6). Nunca incluye datos de tarjeta.</summary>
public sealed record SagaResponse(
    Guid SagaId,
    Guid PedidoId,
    string Estado,
    IReadOnlyList<PasoSagaResponse> Pasos,
    string? Error);

public sealed record PasoSagaResponse(
    string Nombre,
    string Estado,
    DateTime FechaUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Motivo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Resultado);
