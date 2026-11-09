using System.Globalization;
using Logistica.Service.Abstracciones;
using Logistica.Service.Datos;
using Logistica.Service.Handlers;
using Logistica.Service.Mensajeria;
using Logistica.Service.Reglas;
using Microsoft.Extensions.Options;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ----- PostgreSQL (esquema logistica, usuario logistica_app) -----
var cadenaConexion = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("Falta la cadena de conexión 'Postgres'.");
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(cadenaConexion));
builder.Services.AddScoped<IEnviosRepository, EnviosRepository>();
builder.Services.AddSingleton<ConsultasLogistica>();

// ----- RabbitMQ: conexión, publicador de respuestas y consumidor de logistica.comandos -----
builder.Services.Configure<OpcionesRabbitMq>(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<ConexionRabbitMq>();
builder.Services.AddSingleton<IRespuestaPublisher, RespuestaPublisher>();
builder.Services.AddHostedService<LogisticaComandosConsumer>();

// ----- Reglas y handlers. CupoDiario se lee con IOptions desde appsettings.json -----
builder.Services.Configure<OpcionesLogistica>(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CalendarioHabil>();
builder.Services.AddScoped<ProgramarEnvioHandler>();
builder.Services.AddScoped<AnularEnvioHandler>();

builder.Services.AddProblemDetails();

var app = builder.Build();

// ----- API REST de consulta (Minimal APIs): solo leen -----
app.MapGet("/api/envios/{envioId}", async (string envioId, ConsultasLogistica consultas, CancellationToken ct) =>
    await consultas.ObtenerAsync(envioId, ct) is { } envio
        ? Results.Ok(envio)
        : Results.Problem(
            type: "https://tiendanorte/errores/no-encontrado",
            title: "Recurso no encontrado",
            statusCode: StatusCodes.Status404NotFound,
            detail: $"No existe el envío {envioId}."));

app.MapGet("/api/envios", async (string? fecha, ConsultasLogistica consultas, IOptions<OpcionesLogistica> opciones, CancellationToken ct) =>
{
    if (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia))
        return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["fecha"] = ["La fecha debe tener formato AAAA-MM-DD."] },
            type: "https://tiendanorte/errores/formato-invalido");

    var envios = await consultas.ListarPorFechaAsync(dia, ct);
    var programados = envios.Count(e => e.Estado == "Programado");
    return Results.Ok(new EnviosDelDiaDto(dia, opciones.Value.CupoDiario, programados, envios));
});

app.Run();
