namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Consultas de telemetria funcional para consumo de la API publica.
/// </summary>
public interface ITelemetryQueryRepository
{
    /// <summary>
    /// Obtiene el resumen de telemetria de los dispositivos vinculados a un usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario propietario.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Dispositivos vinculados con su ultimo estado conocido.</returns>
    Task<IReadOnlyList<TelemetryDeviceSummaryDto>> GetDeviceSummariesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Indica si el usuario tiene un vinculo activo con el IMEI indicado.
    /// </summary>
    /// <param name="userId">Identificador del usuario propietario.</param>
    /// <param name="imei">IMEI a validar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si el vinculo activo existe.</returns>
    Task<bool> HasActiveDeviceBindingAsync(Guid userId, string imei, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene el historial de posiciones de un IMEI dentro de una ventana temporal.
    /// </summary>
    /// <param name="userId">Identificador del usuario propietario.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="fromUtc">Inicio UTC del rango.</param>
    /// <param name="toUtc">Fin UTC del rango.</param>
    /// <param name="limit">Cantidad maxima de filas.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Historial de posiciones georreferenciadas.</returns>
    Task<IReadOnlyList<DevicePositionPointDto>> GetPositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene eventos recientes de un IMEI dentro de una ventana temporal.
    /// </summary>
    /// <param name="userId">Identificador del usuario propietario.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="fromUtc">Inicio UTC del rango.</param>
    /// <param name="toUtc">Fin UTC del rango.</param>
    /// <param name="limit">Cantidad maxima de filas.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Eventos recientes del dispositivo.</returns>
    Task<IReadOnlyList<DeviceEventDto>> GetEventsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene puntos candidatos para construir recorridos vehiculares dentro de una ventana temporal.
    /// </summary>
    /// <param name="userId">Identificador del usuario propietario.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="fromUtc">Inicio UTC del rango.</param>
    /// <param name="toUtc">Fin UTC del rango.</param>
    /// <param name="maxPoints">Cantidad maxima de puntos candidatos.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Secuencia ascendente de puntos validos para segmentar recorridos.</returns>
    Task<IReadOnlyList<DevicePositionPointDto>> GetTripCandidatePositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int maxPoints,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene eventos ACC (ACC_ON / ACC_OFF) de un IMEI dentro de una ventana temporal.
    /// Usado para anotar posiciones candidatas con el estado de encendido del vehiculo.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="fromUtc">Inicio UTC del rango.</param>
    /// <param name="toUtc">Fin UTC del rango.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Eventos ACC ordenados cronologicamente de forma ascendente.</returns>
    Task<IReadOnlyList<AccEventDto>> GetAccEventsForWindowAsync(
        string imei,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}
