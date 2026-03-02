namespace ImpiTrack.Shared.Api;

/// <summary>
/// Envelope uniforme de salida para todas las respuestas de la API.
/// </summary>
/// <typeparam name="T">Tipo de dato de la respuesta.</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>
    /// Indica si la operacion fue exitosa.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Datos de salida cuando la operacion fue exitosa.
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Informacion de error cuando la operacion falla.
    /// </summary>
    public ApiError? Error { get; init; }

    /// <summary>
    /// Identificador de traza de la solicitud.
    /// </summary>
    public required string TraceId { get; init; }
}
