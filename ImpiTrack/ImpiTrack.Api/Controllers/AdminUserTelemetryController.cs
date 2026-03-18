using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints administrativos de telemetria dentro del contexto de un usuario.
/// </summary>
[ApiController]
[Route("api/admin/users/{userId:guid}/telemetry")]
[Authorize(Policy = "Admin")]
public sealed class AdminUserTelemetryController : ControllerBase
{
    private readonly ITelemetryQueryService _telemetryQueryService;

    /// <summary>
    /// Crea un controlador administrativo de telemetria.
    /// </summary>
    /// <param name="telemetryQueryService">Casos de uso de lectura de telemetria.</param>
    public AdminUserTelemetryController(ITelemetryQueryService telemetryQueryService)
    {
        _telemetryQueryService = telemetryQueryService;
    }

    /// <summary>
    /// Lista el resumen de telemetria de los dispositivos vinculados al usuario indicado.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resumen de dispositivos vinculados.</returns>
    [HttpGet("devices")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TelemetryDeviceSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TelemetryDeviceSummaryDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TelemetryDeviceSummaryDto>>>> GetDevices(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TelemetryDeviceSummaryDto>? data = await _telemetryQueryService.GetDeviceSummariesAsync(userId, cancellationToken);
        if (data is null)
        {
            return UserNotFoundEnvelope<IReadOnlyList<TelemetryDeviceSummaryDto>>();
        }

        return this.OkEnvelope(data);
    }

    /// <summary>
    /// Obtiene el historial de posiciones de un IMEI dentro del contexto del usuario indicado.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="from">Inicio UTC opcional del rango.</param>
    /// <param name="to">Fin UTC opcional del rango.</param>
    /// <param name="limit">Cantidad maxima opcional de filas.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Historial de posiciones georreferenciadas.</returns>
    [HttpGet("devices/{imei}/positions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DevicePositionPointDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DevicePositionPointDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DevicePositionPointDto>>>> GetPositions(
        [FromRoute] Guid userId,
        [FromRoute] string imei,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>> result = await _telemetryQueryService.GetPositionsAsync(
            userId,
            imei,
            from,
            to,
            limit,
            cancellationToken);

        return result.Status switch
        {
            TelemetryLookupStatus.Success => this.OkEnvelope(result.Data ?? []),
            TelemetryLookupStatus.DeviceBindingNotFound => DeviceBindingNotFoundEnvelope<IReadOnlyList<DevicePositionPointDto>>(),
            _ => UserNotFoundEnvelope<IReadOnlyList<DevicePositionPointDto>>()
        };
    }

    /// <summary>
    /// Obtiene eventos recientes de un IMEI dentro del contexto del usuario indicado.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="from">Inicio UTC opcional del rango.</param>
    /// <param name="to">Fin UTC opcional del rango.</param>
    /// <param name="limit">Cantidad maxima opcional de filas.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Eventos recientes del dispositivo.</returns>
    [HttpGet("devices/{imei}/events")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DeviceEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DeviceEventDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DeviceEventDto>>>> GetEvents(
        [FromRoute] Guid userId,
        [FromRoute] string imei,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        TelemetryLookupResult<IReadOnlyList<DeviceEventDto>> result = await _telemetryQueryService.GetEventsAsync(
            userId,
            imei,
            from,
            to,
            limit,
            cancellationToken);

        return result.Status switch
        {
            TelemetryLookupStatus.Success => this.OkEnvelope(result.Data ?? []),
            TelemetryLookupStatus.DeviceBindingNotFound => DeviceBindingNotFoundEnvelope<IReadOnlyList<DeviceEventDto>>(),
            _ => UserNotFoundEnvelope<IReadOnlyList<DeviceEventDto>>()
        };
    }

    /// <summary>
    /// Obtiene recorridos vehiculares construidos desde la telemetria historica del IMEI indicado.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="from">Inicio UTC opcional del rango.</param>
    /// <param name="to">Fin UTC opcional del rango.</param>
    /// <param name="limit">Cantidad maxima opcional de recorridos.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Lista de recorridos construidos para el dispositivo.</returns>
    [HttpGet("devices/{imei}/trips")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TripSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TripSummaryDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TripSummaryDto>>>> GetTrips(
        [FromRoute] Guid userId,
        [FromRoute] string imei,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        TelemetryLookupResult<IReadOnlyList<TripSummaryDto>> result = await _telemetryQueryService.GetTripsAsync(
            userId,
            imei,
            from,
            to,
            limit,
            cancellationToken);

        return result.Status switch
        {
            TelemetryLookupStatus.Success => this.OkEnvelope(result.Data ?? []),
            TelemetryLookupStatus.DeviceBindingNotFound => DeviceBindingNotFoundEnvelope<IReadOnlyList<TripSummaryDto>>(),
            _ => UserNotFoundEnvelope<IReadOnlyList<TripSummaryDto>>()
        };
    }

    /// <summary>
    /// Obtiene el detalle completo de un recorrido vehicular del IMEI indicado.
    /// </summary>
    /// <param name="userId">Identificador del usuario objetivo.</param>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="tripId">Identificador deterministico del recorrido.</param>
    /// <param name="from">Inicio UTC opcional del rango.</param>
    /// <param name="to">Fin UTC opcional del rango.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Detalle del recorrido solicitado.</returns>
    [HttpGet("devices/{imei}/trips/{tripId}")]
    [ProducesResponseType(typeof(ApiResponse<TripDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TripDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<TripDetailDto>>> GetTripById(
        [FromRoute] Guid userId,
        [FromRoute] string imei,
        [FromRoute] string tripId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        TelemetryLookupResult<TripDetailDto> result = await _telemetryQueryService.GetTripByIdAsync(
            userId,
            imei,
            tripId,
            from,
            to,
            cancellationToken);

        return result.Status switch
        {
            TelemetryLookupStatus.Success => this.OkEnvelope(result.Data!),
            TelemetryLookupStatus.DeviceBindingNotFound => DeviceBindingNotFoundEnvelope<TripDetailDto>(),
            TelemetryLookupStatus.TripNotFound => TripNotFoundEnvelope<TripDetailDto>(),
            _ => UserNotFoundEnvelope<TripDetailDto>()
        };
    }

    private ActionResult<ApiResponse<T>> UserNotFoundEnvelope<T>()
    {
        return this.FailEnvelope<T>(
            StatusCodes.Status404NotFound,
            "user_not_found",
            "No existe la cuenta solicitada.");
    }

    private ActionResult<ApiResponse<T>> DeviceBindingNotFoundEnvelope<T>()
    {
        return this.FailEnvelope<T>(
            StatusCodes.Status404NotFound,
            "device_binding_not_found",
            "No existe un vinculo activo para el IMEI indicado.");
    }

    private ActionResult<ApiResponse<T>> TripNotFoundEnvelope<T>()
    {
        return this.FailEnvelope<T>(
            StatusCodes.Status404NotFound,
            "trip_not_found",
            "No existe el recorrido solicitado para el IMEI indicado.");
    }
}
