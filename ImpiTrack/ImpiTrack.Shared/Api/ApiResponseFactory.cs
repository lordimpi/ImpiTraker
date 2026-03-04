namespace ImpiTrack.Shared.Api;

/// <summary>
/// Fabrica de envelopes API para respuestas exitosas y de error.
/// </summary>
public static class ApiResponseFactory
{
    /// <summary>
    /// Crea una respuesta exitosa con datos.
    /// </summary>
    /// <typeparam name="T">Tipo de datos de salida.</typeparam>
    /// <param name="data">Datos de la respuesta.</param>
    /// <param name="traceId">Identificador de traza.</param>
    /// <returns>Envelope de exito.</returns>
    public static ApiResponse<T> Success<T>(T data, string traceId)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Error = null,
            TraceId = traceId
        };
    }

    /// <summary>
    /// Crea una respuesta exitosa sin datos.
    /// </summary>
    /// <param name="traceId">Identificador de traza.</param>
    /// <returns>Envelope de exito.</returns>
    public static ApiResponse<object?> Success(string traceId)
    {
        return new ApiResponse<object?>
        {
            Success = true,
            Data = null,
            Error = null,
            TraceId = traceId
        };
    }

    /// <summary>
    /// Crea una respuesta de error.
    /// </summary>
    /// <typeparam name="T">Tipo esperado por contrato.</typeparam>
    /// <param name="code">Codigo tecnico de error.</param>
    /// <param name="message">Mensaje legible.</param>
    /// <param name="traceId">Identificador de traza.</param>
    /// <param name="details">Detalle opcional del error.</param>
    /// <returns>Envelope de error.</returns>
    public static ApiResponse<T> Failure<T>(string code, string message, string traceId, object? details = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Data = default,
            Error = new ApiError(code, message, details),
            TraceId = traceId
        };
    }
}
