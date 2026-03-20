using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.Shared.Api;
using ImpiTrack.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints para autogestion de cuenta del usuario autenticado.
/// </summary>
[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController : ControllerBase
{
    private readonly IMeAccountService _meAccountService;

    /// <summary>
    /// Crea un controlador de autogestion de usuario.
    /// </summary>
    /// <param name="meAccountService">Casos de uso de cuenta del usuario.</param>
    public MeController(IMeAccountService meAccountService)
    {
        _meAccountService = meAccountService;
    }

    /// <summary>
    /// Obtiene el resumen de cuenta del usuario autenticado.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resumen de cuenta con plan y uso de GPS.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserAccountSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserAccountSummary>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UserAccountSummary>>> GetSummary(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return this.FailEnvelope<UserAccountSummary>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        UserAccountSummary? summary = await _meAccountService.GetSummaryAsync(userId, cancellationToken);
        if (summary is null)
        {
            return this.FailEnvelope<UserAccountSummary>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        return this.OkEnvelope(summary);
    }

    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100];

    /// <summary>
    /// Obtiene los GPS vinculados al usuario autenticado, paginados.
    /// </summary>
    /// <param name="page">Numero de pagina (base 1).</param>
    /// <param name="pageSize">Tamano de pagina (10, 20, 50 o 100).</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resultado paginado de dispositivos vinculados.</returns>
    [HttpGet("devices")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDeviceBinding>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDeviceBinding>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDeviceBinding>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDeviceBinding>>>> GetDevices(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return this.FailEnvelope<PagedResult<UserDeviceBinding>>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        if (!AllowedPageSizes.Contains(pageSize))
        {
            return this.FailEnvelope<PagedResult<UserDeviceBinding>>(
                StatusCodes.Status400BadRequest,
                "invalid_page_size",
                $"El tamano de pagina debe ser uno de: {string.Join(", ", AllowedPageSizes)}.");
        }

        PagedResult<UserDeviceBinding>? result = await _meAccountService.GetDevicesPagedAsync(
            userId,
            new MeDeviceListQuery(page, pageSize),
            cancellationToken);

        if (result is null)
        {
            return this.FailEnvelope<PagedResult<UserDeviceBinding>>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        return this.OkEnvelope(result);
    }

    /// <summary>
    /// Vincula un IMEI a la cuenta del usuario autenticado.
    /// </summary>
    /// <param name="request">IMEI a vincular.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Estado de la operacion de vinculacion.</returns>
    [HttpPost("devices")]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<BindDeviceResult>>> BindDevice(
        [FromBody] BindDeviceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        BindDeviceResult? result = await _meAccountService.BindDeviceAsync(
            userId,
            request.Imei,
            cancellationToken);

        if (result is null)
        {
            return this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        return result.Status switch
        {
            BindDeviceStatus.Bound => this.OkEnvelope(result),
            BindDeviceStatus.AlreadyBound => this.OkEnvelope(result),
            BindDeviceStatus.QuotaExceeded => this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status409Conflict,
                "plan_quota_exceeded",
                "El plan activo no permite vincular mas dispositivos."),
            BindDeviceStatus.OwnedByAnotherUser => this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status409Conflict,
                "imei_owned_by_another_user",
                "El IMEI indicado ya esta vinculado a otro usuario."),
            BindDeviceStatus.MissingActivePlan => this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status409Conflict,
                "missing_active_plan",
                "No existe una suscripcion activa para vincular dispositivos."),
            _ => this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status400BadRequest,
                "device_bind_invalid",
                "No fue posible procesar la vinculacion solicitada.")
        };
    }

    /// <summary>
    /// Desvincula un IMEI de la cuenta del usuario autenticado.
    /// </summary>
    /// <param name="imei">IMEI a desvincular.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resultado de desvinculacion.</returns>
    [HttpDelete("devices/{imei}")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<object?>>> UnbindDevice([FromRoute] string imei, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return this.FailEnvelope<object?>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.");
        }

        UnbindDeviceStatus result = await _meAccountService.UnbindDeviceAsync(userId, imei, cancellationToken);
        return result switch
        {
            UnbindDeviceStatus.Removed => this.OkEnvelope(),
            UnbindDeviceStatus.BindingNotFound => this.FailEnvelope<object?>(
                StatusCodes.Status404NotFound,
                "device_binding_not_found",
                "No existe un vinculo activo para el IMEI indicado."),
            _ => this.FailEnvelope<object?>(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "No fue posible autenticar la solicitud.")
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
}
