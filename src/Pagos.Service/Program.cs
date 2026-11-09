using Npgsql;
using Pagos.Service.Abstracciones;
using Pagos.Service.Datos;
using Pagos.Service.Handlers;
using Pagos.Service.Mensajeria;
using Pagos.Service.Reglas;

var builder = WebApplication.CreateBuilder(args);

// ----- PostgreSQL (esquema pagos, usuario pagos_app) -----
var cadenaConexion = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'Postgres'.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(cadenaConexion));
builder.Services.AddScoped<IPagosRepository, PagosRepository>();
builder.Services.AddSingleton<ConsultasPagos>();

// ----- RabbitMQ: conexión, publicador de respuestas y consumidor de pagos.comandos -----
builder.Services.Configure<OpcionesRabbitMq>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<ConexionRabbitMq>();
builder.Services.AddSingleton<IRespuestaPublisher, RespuestaPublisher>();
builder.Services.AddHostedService<PagosComandosConsumer>();

// ----- Reglas y handlers -----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ValidadorDePago>();
builder.Services.AddScoped<CobrarHandler>();
builder.Services.AddScoped<ReembolsarHandler>();

builder.Services.AddProblemDetails();

var app = builder.Build();

// ----- API REST de consulta (Minimal APIs): solo leen -----
app.MapGet("/api/pagos/{pagoId}", async (string pagoId, ConsultasPagos consultas, CancellationToken ct) =>
    await consultas.ObtenerAsync(pagoId, ct) is { } pago
        ? Results.Ok(pago)
        : Results.Problem(
            type: "https://tiendanorte/errores/no-encontrado",
            title: "Recurso no encontrado",
            statusCode: StatusCodes.Status404NotFound,
            detail: $"No existe el pago {pagoId}."));

// 200 con lista vacía si el pedido no tiene pagos
app.MapGet("/api/pagos", async (Guid pedidoId, ConsultasPagos consultas, CancellationToken ct) =>
    Results.Ok(await consultas.ListarPorPedidoAsync(pedidoId, ct)));

app.Run();
