using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
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
    private static readonly HashSet<string> AllowedSortBy = new(StringComparer.OrdinalIgnoreCase)
    {
        "email",
        "fullName",
        "planCode",
        "maxGps",
        "usedGps",
        "createdAt"
    };

    private static readonly HashSet<string> AllowedSortDirection = new(StringComparer.OrdinalIgnoreCase)
    {
        "asc",
        "desc"
    };

    private static readonly HashSet<string> AllowedDeviceSortBy = new(StringComparer.OrdinalIgnoreCase)
    {
        "imei",
        "boundAtUtc",
        "alias"
    };

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
    /// Lista usuarios con su plan actual y uso de cuota GPS en formato paginado.
    /// </summary>
    /// <param name="page">Pagina solicitada (base 1).</param>
    /// <param name="pageSize">Tamano de pagina.</param>
    /// <param name="search">Busqueda parcial por correo o nombre.</param>
    /// <param name="planCode">Filtro exacto por codigo de plan.</param>
    /// <param name="sortBy">Campo de ordenamiento permitido.</param>
    /// <param name="sortDirection">Direccion de ordenamiento: asc o desc.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Listado administrativo de usuarios.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserAccountOverview>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserAccountOverview>>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserAccountOverview>>>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? planCode = null,
        [FromQuery] string sortBy = "email",
        [FromQuery] string sortDirection = "asc",
        CancellationToken cancellationToken = default)
    {
        if (!AllowedSortBy.Contains(sortBy))
        {
            return this.FailEnvelope<PagedResult<UserAccountOverview>>(
                StatusCodes.Status400BadRequest,
                "invalid_sort_by",
                "El campo de ordenamiento solicitado no es valido.");
        }

        if (!AllowedSortDirection.Contains(sortDirection))
        {
            return this.FailEnvelope<PagedResult<UserAccountOverview>>(
                StatusCodes.Status400BadRequest,
                "invalid_sort_direction",
                "La direccion de ordenamiento solicitada no es valida.");
        }

        var query = new AdminUserListQuery(page, pageSize, search, planCode, sortBy, sortDirection);
        PagedResult<UserAccountOverview> users = await _adminUsersService.GetUsersAsync(query, cancellationToken);
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
    /// Obtiene los dispositivos vinculados a un usuario especifico en formato paginado.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="page">Pagina solicitada (base 1).</param>
    /// <param name="pageSize">Tamano de pagina.</param>
    /// <param name="search">Texto parcial para filtrar por IMEI o alias.</param>
    /// <param name="sortBy">Campo de ordenamiento permitido.</param>
    /// <param name="sortDirection">Direccion de ordenamiento: asc o desc.</param>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Listado paginado de dispositivos activos del usuario.</returns>
    [HttpGet("{userId:guid}/devices")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDeviceBinding>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDeviceBinding>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UserDeviceBinding>>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<UserDeviceBinding>>>> GetUserDevices(
        [FromRoute] Guid userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string sortBy = "boundAtUtc",
        [FromQuery] string sortDirection = "desc",
        CancellationToken cancellationToken = default)
    {
        if (!AllowedDeviceSortBy.Contains(sortBy))
        {
            return this.FailEnvelope<PagedResult<UserDeviceBinding>>(
                StatusCodes.Status400BadRequest,
                "invalid_sort_by",
                "El campo de ordenamiento solicitado no es valido.");
        }

        if (!AllowedSortDirection.Contains(sortDirection))
        {
            return this.FailEnvelope<PagedResult<UserDeviceBinding>>(
                StatusCodes.Status400BadRequest,
                "invalid_sort_direction",
                "La direccion de ordenamiento solicitada no es valida.");
        }

        var query = new AdminDeviceListQuery(page, pageSize, sortBy, sortDirection, search);
        PagedResult<UserDeviceBinding>? devices = await _adminUsersService.GetUserDevicesAsync(userId, query, cancellationToken);

        if (devices is null)
        {
            return this.FailEnvelope<PagedResult<UserDeviceBinding>>(
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
