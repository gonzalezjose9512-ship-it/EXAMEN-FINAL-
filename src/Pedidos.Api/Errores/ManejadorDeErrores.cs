using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Pedidos.Application.Excepciones;
using Pedidos.Domain.Excepciones;

namespace Pedidos.Api.Errores;

/// <summary>
/// Traduce las excepciones conocidas a respuestas ProblemDetails:
///   NoEncontradoException   → 404
///   EstadoInvalidoException → 409
///   ConcurrenciaException   → 409
///   ReglaNegocioException   → 422
/// Cualquier otra excepción la maneja ASP.NET Core como 500.
/// Así los controllers no tienen try/catch ni lógica de errores.
/// </summary>
public sealed class ManejadorDeErrores(IProblemDetailsService problemDetailsService, ILogger<ManejadorDeErrores> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, tipo, titulo) = exception switch
        {
            NoEncontradoException => (StatusCodes.Status404NotFound, "no-encontrado", "Recurso no encontrado"),
            EstadoInvalidoException => (StatusCodes.Status409Conflict, "estado-invalido", "Operación no permitida"),
            ConcurrenciaException => (StatusCodes.Status409Conflict, "conflicto-de-version", "Conflicto de versión"),
            ReglaNegocioException => (StatusCodes.Status422UnprocessableEntity, "regla-de-negocio", "Regla de negocio no cumplida"),
            _ => (0, string.Empty, string.Empty)
        };

        if (status == 0)
            return false; // no es una excepción conocida: 500

        logger.LogInformation("{Titulo}: {Detalle}", titulo, exception.Message);

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Type = $"https://tiendanorte/errores/{tipo}",
                Title = titulo,
                Status = status,
                Detail = exception.Message
            }
        });
    }
}
