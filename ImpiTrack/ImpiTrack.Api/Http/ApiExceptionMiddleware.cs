using ImpiTrack.Shared.Api;

namespace ImpiTrack.Api.Http;

/// <summary>
/// Middleware global para excepciones no controladas con salida ApiResponse.
/// </summary>
public sealed class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    /// <summary>
    /// Crea middleware para manejar excepciones en pipeline HTTP.
    /// </summary>
    /// <param name="next">Siguiente middleware.</param>
    /// <param name="logger">Logger estructurado.</param>
    public ApiExceptionMiddleware(
        RequestDelegate next,
        ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta la captura global de excepciones.
    /// </summary>
    /// <param name="context">Contexto HTTP.</param>
    /// <returns>Tarea asincronica.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            _logger.LogWarning("api_request_cancelled path={path}", context.Request.Path);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "api_unhandled_exception path={path}", context.Request.Path);
            await ApiProblemDetailsFactory.WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Se produjo un error inesperado al procesar la solicitud.");
        }
    }
}
