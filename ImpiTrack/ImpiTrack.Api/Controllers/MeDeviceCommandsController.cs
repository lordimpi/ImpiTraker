using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints para el ciclo de vida de comandos de dispositivo del usuario autenticado.
/// </summary>
[ApiController]
[Route("api/me/devices/{imei}/commands")]
[Authorize]
public sealed class MeDeviceCommandsController : ControllerBase
{
    private readonly IDeviceCommandService _service;

    /// <summary>
    /// Crea un controlador de comandos de dispositivo para el usuario autenticado.
    /// </summary>
    /// <param name="service">Servicio de ciclo de vida de comandos.</param>
    public MeDeviceCommandsController(IDeviceCommandService service)
    {
        _service = service;
    }

    /// <summary>
    /// Encola un comando de dispositivo para el IMEI indicado.
    /// El comando se despacha de inmediato si el dispositivo esta online; de lo contrario queda encolado
    /// y se entregara automaticamente cuando el dispositivo reconecte.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo destino (debe pertenecer al usuario autenticado).</param>
    /// <param name="request">Tipo de comando, parametros opcionales y confirmacion para comandos peligrosos.</param>
    /// <param name="ct">Token de cancelacion de la solicitud.</param>
    /// <returns>202 Accepted con el identificador y estado inicial del comando.</returns>
    [HttpPost]
    [EnableRateLimiting("device-commands")]
    [ProducesResponseType(typeof(ApiResponse<EnqueueCommandResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<EnqueueCommandResponse>>> Enqueue(
        [FromRoute] string imei,
        [FromBody] EnqueueCommandRequest request,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return UnauthorizedEnvelope<EnqueueCommandResponse>();
        }

        string userIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string userAgent = Request.Headers.UserAgent.ToString();

        EnqueueResult result = await _service.EnqueueAsync(
            imei,
            userId,
            request.Type,
            request.Parameters,
            request.Confirm ?? false,
            userIp,
            userAgent,
            ct);

        if (!result.Success)
        {
            return result.ErrorCode switch
            {
                "not_found" => DeviceNotFoundEnvelope<EnqueueCommandResponse>(),
                "protocol_unknown" => this.FailEnvelope<EnqueueCommandResponse>(
                    StatusCodes.Status400BadRequest,
                    "protocol_unknown",
                    result.ErrorMessage ?? "No fue posible resolver el protocolo del dispositivo."),
                "command_not_supported_by_protocol" => this.FailEnvelope<EnqueueCommandResponse>(
                    StatusCodes.Status400BadRequest,
                    "command_not_supported_by_protocol",
                    result.ErrorMessage ?? "El protocolo del dispositivo no soporta este tipo de comando."),
                "confirmation_required" => this.FailEnvelope<EnqueueCommandResponse>(
                    StatusCodes.Status400BadRequest,
                    "confirmation_required",
                    result.ErrorMessage ?? "Este comando requiere confirmacion explicita (confirm: true)."),
                "invalid_parameters" => this.FailEnvelope<EnqueueCommandResponse>(
                    StatusCodes.Status400BadRequest,
                    "invalid_parameters",
                    result.ErrorMessage ?? "Los parametros del comando son invalidos o estan fuera de rango."),
                _ => this.FailEnvelope<EnqueueCommandResponse>(
                    StatusCodes.Status400BadRequest,
                    result.ErrorCode ?? "command_enqueue_failed",
                    result.ErrorMessage ?? "No fue posible encolar el comando.")
            };
        }

        // device_offline is informational: command persisted as Queued — still a 202
        var response = new EnqueueCommandResponse(
            result.CommandId!.Value,
            "Queued",
            DateTimeOffset.UtcNow);

        return Accepted((object)this.OkEnvelope(response).Value!);
    }

    /// <summary>
    /// Lista los comandos del dispositivo IMEI del usuario autenticado, con paginacion y filtros opcionales.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="status">Filtra por estado del ciclo de vida. Null para todos los estados.</param>
    /// <param name="from">Filtra comandos encolados a partir de esta marca UTC.</param>
    /// <param name="to">Filtra comandos encolados hasta esta marca UTC.</param>
    /// <param name="page">Numero de pagina (base 1).</param>
    /// <param name="pageSize">Cantidad de registros por pagina (10, 20, 50 o 100).</param>
    /// <param name="ct">Token de cancelacion.</param>
    /// <returns>Pagina de comandos del dispositivo.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<DeviceCommandDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<DeviceCommandDto>>>> List(
        [FromRoute] string imei,
        [FromQuery] string? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return UnauthorizedEnvelope<PagedResult<DeviceCommandDto>>();
        }

        if (!AllowedPageSizes.Contains(pageSize))
        {
            return this.FailEnvelope<PagedResult<DeviceCommandDto>>(
                StatusCodes.Status400BadRequest,
                "invalid_page_size",
                $"El tamano de pagina debe ser uno de: {string.Join(", ", AllowedPageSizes)}.");
        }

        var query = new DeviceCommandListQuery(status, from, to, page, pageSize);
        PagedResult<DeviceCommandRecord> paged = await _service.ListAsync(imei, userId, query, ct);

        PagedResult<DeviceCommandDto> result = new(
            paged.Items.Select(MapToDto).ToList(),
            paged.Page,
            paged.PageSize,
            paged.TotalItems,
            paged.TotalPages);

        return this.OkEnvelope(result);
    }

    /// <summary>
    /// Obtiene el estado de un comando especifico del dispositivo IMEI del usuario autenticado.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="commandId">Identificador unico del comando.</param>
    /// <param name="ct">Token de cancelacion.</param>
    /// <returns>Estado actual del comando solicitado.</returns>
    [HttpGet("{commandId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<DeviceCommandDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<DeviceCommandDto>>> GetById(
        [FromRoute] string imei,
        [FromRoute] Guid commandId,
        CancellationToken ct)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return UnauthorizedEnvelope<DeviceCommandDto>();
        }

        DeviceCommandRecord? record = await _service.GetByIdAsync(commandId, userId, ct);
        if (record is null || !string.Equals(record.Imei, imei, StringComparison.OrdinalIgnoreCase))
        {
            return DeviceNotFoundEnvelope<DeviceCommandDto>();
        }

        return this.OkEnvelope(MapToDto(record));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100];

    private static DeviceCommandDto MapToDto(DeviceCommandRecord r) =>
        new(r.CommandId, r.Imei, r.CommandType, r.Status,
            r.QueuedAtUtc, r.SentAtUtc, r.AckedAtUtc,
            r.ResponseCode, r.ResponseText, r.FailureReason);

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

    private ActionResult<ApiResponse<T>> DeviceNotFoundEnvelope<T>()
    {
        return this.FailEnvelope<T>(
            StatusCodes.Status404NotFound,
            "device_binding_not_found",
            "No existe un vinculo activo para el IMEI indicado.");
    }
}
