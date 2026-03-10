using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints de telemetria funcional para el usuario autenticado.
/// </summary>
[ApiController]
[Route("api/me/telemetry")]
[Authorize]
public sealed class MeTelemetryController : ControllerBase
{
    private readonly ITelemetryQueryService _telemetryQueryService;

    /// <summary>
    /// Crea un controlador de telemetria para el usuario autenticado.
    /// </summary>
    /// <param name="telemetryQueryService">Casos de uso de lectura de telemetria.</param>
    public MeTelemetryController(ITelemetryQueryService telemetryQueryService)
    {
        _telemetryQueryService = telemetryQueryService;
    }

    /// <summary>
    /// Lista el resumen de telemetria de los dispositivos vinculados al usuario autenticado.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resumen de dispositivos vinculados.</returns>
    [HttpGet("devices")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TelemetryDeviceSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TelemetryDeviceSummaryDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TelemetryDeviceSummaryDto>>>> GetDevices(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return UnauthorizedEnvelope<IReadOnlyList<TelemetryDeviceSummaryDto>>();
        }

        IReadOnlyList<TelemetryDeviceSummaryDto>? data = await _telemetryQueryService.GetDeviceSummariesAsync(userId, cancellationToken);
        if (data is null)
        {
            return UnauthorizedEnvelope<IReadOnlyList<TelemetryDeviceSummaryDto>>();
        }

        return this.OkEnvelope(data);
    }

    /// <summary>
    /// Obtiene el historial de posiciones de un IMEI propio.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="from">Inicio UTC opcional del rango.</param>
    /// <param name="to">Fin UTC opcional del rango.</param>
    /// <param name="limit">Cantidad maxima opcional de filas.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Historial de posiciones georreferenciadas.</returns>
    [HttpGet("devices/{imei}/positions")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DevicePositionPointDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DevicePositionPointDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DevicePositionPointDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DevicePositionPointDto>>>> GetPositions(
        [FromRoute] string imei,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return UnauthorizedEnvelope<IReadOnlyList<DevicePositionPointDto>>();
        }

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
            _ => UnauthorizedEnvelope<IReadOnlyList<DevicePositionPointDto>>()
        };
    }

    /// <summary>
    /// Obtiene eventos recientes de un IMEI propio.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="from">Inicio UTC opcional del rango.</param>
    /// <param name="to">Fin UTC opcional del rango.</param>
    /// <param name="limit">Cantidad maxima opcional de filas.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Eventos recientes del dispositivo.</returns>
    [HttpGet("devices/{imei}/events")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DeviceEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DeviceEventDto>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<DeviceEventDto>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DeviceEventDto>>>> GetEvents(
        [FromRoute] string imei,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return UnauthorizedEnvelope<IReadOnlyList<DeviceEventDto>>();
        }

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
            _ => UnauthorizedEnvelope<IReadOnlyList<DeviceEventDto>>()
        };
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        string? rawId =
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue("sub");

        return Guid.TryParse(rawId, out userId);
    }

    private ActionResult<ApiResponse<T>> UnauthorizedEnvelope<T>()
    {
        return this.FailEnvelope<T>(
            StatusCodes.Status401Unauthorized,
            "unauthorized",
            "No fue posible autenticar la solicitud.");
    }

    private ActionResult<ApiResponse<T>> DeviceBindingNotFoundEnvelope<T>()
    {
        return this.FailEnvelope<T>(
            StatusCodes.Status404NotFound,
            "device_binding_not_found",
            "No existe un vinculo activo para el IMEI indicado.");
    }
}
