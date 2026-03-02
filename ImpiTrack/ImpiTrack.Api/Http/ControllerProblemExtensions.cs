using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Http;

/// <summary>
/// Extensiones para emitir envelopes ApiResponse desde controladores.
/// </summary>
internal static class ControllerProblemExtensions
{
    /// <summary>
    /// Crea respuesta de exito con datos.
    /// </summary>
    /// <typeparam name="T">Tipo de datos.</typeparam>
    /// <param name="controller">Controlador actual.</param>
    /// <param name="data">Datos de salida.</param>
    /// <returns>Resultado HTTP 200 con envelope estandar.</returns>
    internal static ActionResult<ApiResponse<T>> OkEnvelope<T>(
        this ControllerBase controller,
        T data)
    {
        ApiResponse<T> payload = ApiResponseFactory.Success(data, controller.HttpContext.TraceIdentifier);
        return controller.Ok(payload);
    }

    /// <summary>
    /// Crea respuesta de exito sin datos.
    /// </summary>
    /// <param name="controller">Controlador actual.</param>
    /// <returns>Resultado HTTP 200 con envelope estandar.</returns>
    internal static ActionResult<ApiResponse<object?>> OkEnvelope(this ControllerBase controller)
    {
        ApiResponse<object?> payload = ApiResponseFactory.Success(controller.HttpContext.TraceIdentifier);
        return controller.Ok(payload);
    }

    /// <summary>
    /// Crea respuesta de error estandarizada.
    /// </summary>
    /// <typeparam name="T">Tipo de datos esperado por contrato.</typeparam>
    /// <param name="controller">Controlador actual.</param>
    /// <param name="statusCode">Codigo HTTP.</param>
    /// <param name="code">Codigo tecnico de error.</param>
    /// <param name="message">Mensaje legible.</param>
    /// <param name="details">Detalle opcional.</param>
    /// <returns>Resultado con envelope de error.</returns>
    internal static ActionResult<ApiResponse<T>> FailEnvelope<T>(
        this ControllerBase controller,
        int statusCode,
        string code,
        string message,
        object? details = null)
    {
        ApiResponse<T> payload = ApiProblemDetailsFactory.CreateError<T>(
            controller.HttpContext,
            code,
            message,
            details);

        return new ObjectResult(payload)
        {
            StatusCode = statusCode
        };
    }
}
