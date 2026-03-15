using ImpiTrack.DataAccess.Abstractions;

namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Casos de uso para consulta funcional de telemetria por dispositivo.
/// </summary>
public interface ITelemetryQueryService
{
    /// <summary>
    /// Obtiene el resumen de telemetria de los dispositivos vinculados a un usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resumen o <c>null</c> si el usuario no existe.</returns>
    Task<IReadOnlyList<TelemetryDeviceSummaryDto>?> GetDeviceSummariesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene el historial de posiciones de un dispositivo.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="fromUtc">Inicio UTC opcional.</param>
    /// <param name="toUtc">Fin UTC opcional.</param>
    /// <param name="limit">Limite opcional de resultados.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado con estado y datos.</returns>
    Task<TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>> GetPositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene eventos recientes de un dispositivo.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="fromUtc">Inicio UTC opcional.</param>
    /// <param name="toUtc">Fin UTC opcional.</param>
    /// <param name="limit">Limite opcional de resultados.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado con estado y datos.</returns>
    Task<TelemetryLookupResult<IReadOnlyList<DeviceEventDto>>> GetEventsAsync(
        Guid userId,
        string imei,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene recorridos vehiculares construidos desde la telemetria de un dispositivo.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="fromUtc">Inicio UTC opcional.</param>
    /// <param name="toUtc">Fin UTC opcional.</param>
    /// <param name="limit">Cantidad maxima opcional de recorridos.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado con estado y recorridos encontrados.</returns>
    Task<TelemetryLookupResult<IReadOnlyList<TripSummaryDto>>> GetTripsAsync(
        Guid userId,
        string imei,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene el detalle completo de un recorrido vehicular.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="tripId">Identificador deterministico del recorrido.</param>
    /// <param name="fromUtc">Inicio UTC opcional.</param>
    /// <param name="toUtc">Fin UTC opcional.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado con estado y detalle del recorrido.</returns>
    Task<TelemetryLookupResult<TripDetailDto>> GetTripByIdAsync(
        Guid userId,
        string imei,
        string tripId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken);
}
