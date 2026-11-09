using Inventario.Service.Abstracciones;
using Inventario.Service.Datos;
using Inventario.Service.Handlers;
using Inventario.Service.Mensajeria;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ----- PostgreSQL (esquema inventario, usuario inventario_app) -----
var cadenaConexion = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'Postgres'.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(cadenaConexion));
builder.Services.AddScoped<IInventarioRepository, InventarioRepository>();
builder.Services.AddSingleton<ConsultasInventario>();

// ----- RabbitMQ: conexión, publicador de respuestas y consumidor de inventario.comandos -----
builder.Services.Configure<OpcionesRabbitMq>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<ConexionRabbitMq>();
builder.Services.AddSingleton<IRespuestaPublisher, RespuestaPublisher>();
builder.Services.AddHostedService<InventarioComandosConsumer>();

// ----- Handlers (lógica de cada comando) -----
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ReservarStockHandler>();
builder.Services.AddScoped<LiberarStockHandler>();

builder.Services.AddProblemDetails();

var app = builder.Build();

// Datos iniciales obligatorios (P001 a P005), cargados al arrancar
await DatosIniciales.CargarAsync(app.Services, app.Logger);

// ----- API REST de consulta (Minimal APIs): solo leen -----
app.MapGet("/api/productos", async (ConsultasInventario consultas, CancellationToken ct) =>
    Results.Ok(await consultas.ListarProductosAsync(ct)));

app.MapGet("/api/productos/{productoId}", async (string productoId, ConsultasInventario consultas, CancellationToken ct) =>
    await consultas.ObtenerProductoAsync(productoId, ct) is { } producto
        ? Results.Ok(producto)
        : NoEncontrado($"No existe el producto {productoId}."));

app.MapGet("/api/reservas/{reservaId}", async (string reservaId, ConsultasInventario consultas, CancellationToken ct) =>
    await consultas.ObtenerReservaAsync(reservaId, ct) is { } reserva
        ? Results.Ok(reserva)
        : NoEncontrado($"No existe la reserva {reservaId}."));

app.Run();

static IResult NoEncontrado(string detalle) => Results.Problem(
    type: "https://tiendanorte/errores/no-encontrado",
    title: "Recurso no encontrado",
    statusCode: StatusCodes.Status404NotFound,
    detail: detalle);
