using Pedidos.Api.Errores;
using Pedidos.Application.CasosDeUso;
using Pedidos.Application.Sagas;
using Pedidos.Infrastructure.Mensajeria;
using Pedidos.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ----- Adaptador de entrada HTTP: controllers + Swagger -----
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ----- Errores en formato ProblemDetails (404, 409, 422) -----
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ManejadorDeErrores>();

// ----- Casos de uso (Application) -----
builder.Services.AddScoped<CrearPedido>();
builder.Services.AddScoped<AgregarItem>();
builder.Services.AddScoped<ObtenerPedido>();
builder.Services.AddScoped<ObtenerEventos>();
builder.Services.AddScoped<ObtenerSaga>();
builder.Services.AddScoped<ConfirmarPedido>();

// ----- Saga de confirmación y su adaptador de entrada (cola pedidos.respuestas) -----
builder.Services.AddScoped<ConfirmacionPedidoSaga>();
builder.Services.AddHostedService<RespuestasConsumer>();

// Reloj del sistema: las reglas que dependen de la fecha lo reciben por constructor
builder.Services.AddSingleton(TimeProvider.System);

// ----- Adaptadores de salida (Infrastructure): PostgreSQL y RabbitMQ -----
builder.Services.AddInfraestructura(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

// Swagger habilitado en desarrollo (lo pide el enunciado)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
