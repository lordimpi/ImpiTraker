using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Shared.Api;
using ImpiTrack.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints administrativos para gestion de usuarios, planes y vinculos de GPS.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = "Admin")]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAdminUsersService _adminUsersService;

    /// <summary>
    /// Crea un controlador administrativo de usuarios.
    /// </summary>
    /// <param name="adminUsersService">Casos de uso administrativos de usuario.</param>
    public AdminUsersController(IAdminUsersService adminUsersService)
    {
        _adminUsersService = adminUsersService;
    }

    /// <summary>
    /// Lista usuarios con su plan actual y uso de cuota GPS.
    /// </summary>
    /// <param name="limit">Maximo de filas a retornar.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Listado administrativo de usuarios.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserAccountOverview>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserAccountOverview>>>> GetUsers(
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<UserAccountOverview> users = await _adminUsersService.GetUsersAsync(limit, cancellationToken);
        return this.OkEnvelope(users);
    }

    /// <summary>
    /// Obtiene el resumen de cuenta de un usuario especifico.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resumen de cuenta del usuario.</returns>
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserAccountSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserAccountSummary>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UserAccountSummary>>> GetUserSummary(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        UserAccountSummary? summary = await _adminUsersService.GetUserSummaryAsync(userId, cancellationToken);
        if (summary is null)
        {
            return this.FailEnvelope<UserAccountSummary>(
                StatusCodes.Status404NotFound,
                "user_not_found",
                "No existe la cuenta solicitada.");
        }

        return this.OkEnvelope(summary);
    }

    /// <summary>
    /// Obtiene los dispositivos vinculados a un usuario especifico.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Lista de dispositivos activos del usuario.</returns>
    [HttpGet("{userId:guid}/devices")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserDeviceBinding>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UserDeviceBinding>>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserDeviceBinding>>>> GetUserDevices(
        [FromRoute] Guid userId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UserDeviceBinding>? devices = await _adminUsersService.GetUserDevicesAsync(userId, cancellationToken);
        if (devices is null)
        {
            return this.FailEnvelope<IReadOnlyList<UserDeviceBinding>>(
                StatusCodes.Status404NotFound,
                "user_not_found",
                "No existe la cuenta solicitada.");
        }

        return this.OkEnvelope(devices);
    }

    /// <summary>
    /// Asigna un plan activo a un usuario.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="request">Codigo de plan a activar.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resumen actualizado del usuario.</returns>
    [HttpPut("{userId:guid}/plan")]
    [ProducesResponseType(typeof(ApiResponse<UserAccountSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserAccountSummary>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<UserAccountSummary>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<UserAccountSummary>>> SetUserPlan(
        [FromRoute] Guid userId,
        [FromBody] SetUserPlanRequest request,
        CancellationToken cancellationToken)
    {
        SetUserPlanResult result = await _adminUsersService.SetUserPlanAsync(userId, request.PlanCode, cancellationToken);

        return result.Status switch
        {
            SetUserPlanStatus.Updated when result.Summary is not null => this.OkEnvelope(result.Summary),
            SetUserPlanStatus.UserNotFound => this.FailEnvelope<UserAccountSummary>(
                StatusCodes.Status404NotFound,
                "user_not_found",
                "No existe la cuenta solicitada."),
            SetUserPlanStatus.InvalidPlanCode => this.FailEnvelope<UserAccountSummary>(
                StatusCodes.Status400BadRequest,
                "invalid_plan_code",
                "No fue posible asignar el plan solicitado."),
            _ => this.FailEnvelope<UserAccountSummary>(
                StatusCodes.Status404NotFound,
                "user_account_not_found",
                "No existe informacion de cuenta para el usuario solicitado.")
        };
    }

    /// <summary>
    /// Vincula un IMEI a un usuario especifico (operacion administrativa).
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="request">IMEI a vincular.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Estado de la operacion de vinculacion.</returns>
    [HttpPost("{userId:guid}/devices")]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<BindDeviceResult>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<BindDeviceResult>>> BindDeviceForUser(
        [FromRoute] Guid userId,
        [FromBody] BindDeviceRequest request,
        CancellationToken cancellationToken)
    {
        AdminBindDeviceResult result = await _adminUsersService.BindDeviceAsync(userId, request.Imei, cancellationToken);
        if (result.Status == AdminBindDeviceStatus.UserNotFound)
        {
            return this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status404NotFound,
                "user_not_found",
                "No existe la cuenta solicitada.");
        }

        if (result.Binding is null)
        {
            return this.FailEnvelope<BindDeviceResult>(
                StatusCodes.Status400BadRequest,
                "device_bind_invalid",
                "No fue posible procesar la vinculacion solicitada.");
        }

        return result.Binding.Status switch
        {
            BindDeviceStatus.Bound => this.OkEnvelope(result.Binding),
            BindDeviceStatus.AlreadyBound => this.OkEnvelope(result.Binding),
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
    /// Desvincula un IMEI de un usuario especifico.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="imei">IMEI a desvincular.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Resultado de desvinculacion.</returns>
    [HttpDelete("{userId:guid}/devices/{imei}")]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<object?>>> UnbindDeviceForUser(
        [FromRoute] Guid userId,
        [FromRoute] string imei,
        CancellationToken cancellationToken)
    {
        UnbindDeviceStatus result = await _adminUsersService.UnbindDeviceAsync(userId, imei, cancellationToken);
        return result switch
        {
            UnbindDeviceStatus.Removed => this.OkEnvelope(),
            UnbindDeviceStatus.BindingNotFound => this.FailEnvelope<object?>(
                StatusCodes.Status404NotFound,
                "device_binding_not_found",
                "No existe un vinculo activo para el IMEI indicado."),
            _ => this.FailEnvelope<object?>(
                StatusCodes.Status404NotFound,
                "user_not_found",
                "No existe la cuenta solicitada.")
        };
    }
}
