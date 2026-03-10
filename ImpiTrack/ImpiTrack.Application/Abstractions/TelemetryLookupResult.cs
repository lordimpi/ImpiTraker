namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Estados posibles al resolver una consulta de telemetria por IMEI.
/// </summary>
public enum TelemetryLookupStatus
{
    /// <summary>
    /// La consulta fue resuelta correctamente.
    /// </summary>
    Success = 1,

    /// <summary>
    /// El usuario objetivo no existe en Identity.
    /// </summary>
    UserNotFound = 2,

    /// <summary>
    /// El IMEI no tiene un vinculo activo para el usuario objetivo.
    /// </summary>
    DeviceBindingNotFound = 3
}

/// <summary>
/// Resultado tipado de una consulta de telemetria por dispositivo.
/// </summary>
/// <typeparam name="T">Tipo de datos devueltos.</typeparam>
/// <param name="Status">Estado final de la consulta.</param>
/// <param name="Data">Datos asociados cuando aplica.</param>
public sealed record TelemetryLookupResult<T>(
    TelemetryLookupStatus Status,
    T? Data);
