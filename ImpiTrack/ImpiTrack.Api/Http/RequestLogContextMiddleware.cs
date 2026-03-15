using System.Security.Claims;
using Serilog.Context;

namespace ImpiTrack.Api.Http;

/// <summary>
/// Enriquece el contexto de Serilog con datos base de la solicitud HTTP actual.
/// </summary>
public sealed class RequestLogContextMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Crea el middleware que adjunta metadatos de solicitud al contexto de log.
    /// </summary>
    /// <param name="next">Siguiente middleware del pipeline.</param>
    public RequestLogContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Publica propiedades estables de la solicitud para todos los logs emitidos aguas abajo.
    /// </summary>
    /// <param name="context">Contexto HTTP actual.</param>
    /// <returns>Tarea asincronica del pipeline.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        string? userId =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            context.User.FindFirstValue("sub");

        using IDisposable traceScope = LogContext.PushProperty("TraceId", context.TraceIdentifier);
        using IDisposable pathScope = LogContext.PushProperty("RequestPath", context.Request.Path.Value ?? string.Empty);
        using IDisposable methodScope = LogContext.PushProperty("RequestMethod", context.Request.Method);
        using IDisposable userScope = LogContext.PushProperty("UserId", userId ?? "anonymous");

        await _next(context);
    }
}
