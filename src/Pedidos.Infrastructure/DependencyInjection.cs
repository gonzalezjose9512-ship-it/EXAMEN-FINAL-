using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pedidos.Application.Puertos;
using Pedidos.Infrastructure.Persistencia;

namespace Pedidos.Infrastructure;

/// <summary>
/// Registra los adaptadores de salida: conecta cada puerto de Application con su implementación.
/// Es el único lugar donde se decide "IEventStore = PostgresEventStore".
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfraestructura(this IServiceCollection services, IConfiguration configuration)
    {
        // La cadena de conexión sale de appsettings.json o de la variable de entorno
        // ConnectionStrings__Postgres (en Docker). Nunca está escrita en el código.
        var cadenaConexion = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'Postgres' (appsettings.json o variable ConnectionStrings__Postgres).");

        services.AddSingleton(_ => NpgsqlDataSource.Create(cadenaConexion));

        // Una unidad de trabajo por request: los dos repositorios comparten su conexión y transacción
        services.AddScoped<PostgresUnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PostgresUnitOfWork>());
        services.AddScoped<IEventStore, PostgresEventStore>();
        services.AddScoped<ISagaRepository, PostgresSagaRepository>();

        return services;
    }
}
