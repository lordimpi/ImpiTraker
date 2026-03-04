using System.Text.Json;
using ImpiTrack.Shared.Api;

namespace ImpiTrack.Api.Http;

/// <summary>
/// Utilidades para construir y serializar respuestas API estandarizadas.
/// </summary>
internal static class ApiProblemDetailsFactory
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Crea un envelope de error para respuestas HTTP.
    /// </summary>
    /// <typeparam name="T">Tipo esperado por contrato.</typeparam>
    /// <param name="httpContext">Contexto HTTP actual.</param>
    /// <param name="code">Codigo tecnico de error.</param>
    /// <param name="message">Mensaje legible.</param>
    /// <param name="details">Detalle opcional.</param>
    /// <returns>Envelope estandar de error.</returns>
    internal static ApiResponse<T> CreateError<T>(
        HttpContext httpContext,
        string code,
        string message,
        object? details = null)
    {
        return ApiResponseFactory.Failure<T>(
            code,
            message,
            httpContext.TraceIdentifier,
            details);
    }

    /// <summary>
    /// Obtiene el codigo tecnico por defecto para un status HTTP.
    /// </summary>
    /// <param name="statusCode">Codigo de estado HTTP.</param>
    /// <returns>Codigo tecnico de error.</returns>
    internal static string GetDefaultErrorCode(int statusCode)
    {
        return statusCode switch
        {
            StatusCodes.Status400BadRequest => "bad_request",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status404NotFound => "resource_not_found",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status422UnprocessableEntity => "validation_failed",
            StatusCodes.Status500InternalServerError => "internal_error",
            _ => "request_failed"
        };
    }

    /// <summary>
    /// Escribe un envelope de error en la respuesta HTTP.
    /// </summary>
    /// <param name="httpContext">Contexto HTTP actual.</param>
    /// <param name="statusCode">Codigo de estado HTTP.</param>
    /// <param name="code">Codigo tecnico de error.</param>
    /// <param name="message">Mensaje legible.</param>
    /// <param name="details">Detalle opcional.</param>
    /// <returns>Tarea asincronica de escritura.</returns>
    internal static async Task WriteErrorAsync(
        HttpContext httpContext,
        int statusCode,
        string code,
        string message,
        object? details = null)
    {
        if (httpContext.Response.HasStarted)
        {
            return;
        }

        ApiResponse<object?> payload = CreateError<object?>(httpContext, code, message, details);
        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";
        string json = JsonSerializer.Serialize(payload, WebJsonOptions);
        await httpContext.Response.WriteAsync(json);
    }
}
