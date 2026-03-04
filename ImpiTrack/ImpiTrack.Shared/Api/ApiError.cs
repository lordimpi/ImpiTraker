namespace ImpiTrack.Shared.Api;

/// <summary>
/// Representa un error estandar para respuestas HTTP de la API.
/// </summary>
/// <param name="Code">Codigo tecnico de error.</param>
/// <param name="Message">Mensaje legible para cliente.</param>
/// <param name="Details">Detalle opcional para depuracion o validaciones.</param>
public sealed record ApiError(
    string Code,
    string Message,
    object? Details = null);
