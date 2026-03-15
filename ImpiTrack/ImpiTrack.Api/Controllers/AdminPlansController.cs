using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints administrativos para consulta del catalogo de planes.
/// </summary>
[ApiController]
[Route("api/admin/plans")]
[Authorize(Policy = "Admin")]
public sealed class AdminPlansController : ControllerBase
{
    private readonly IAdminUsersService _adminUsersService;

    /// <summary>
    /// Crea un controlador administrativo para catalogo de planes.
    /// </summary>
    /// <param name="adminUsersService">Casos de uso administrativos.</param>
    public AdminPlansController(IAdminUsersService adminUsersService)
    {
        _adminUsersService = adminUsersService;
    }

    /// <summary>
    /// Lista planes activos disponibles para asignacion administrativa.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Catalogo de planes activos.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AdminPlanDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AdminPlanDto>>>> GetPlans(CancellationToken cancellationToken)
    {
        IReadOnlyList<AdminPlanDto> plans = await _adminUsersService.GetPlansAsync(cancellationToken);
        return this.OkEnvelope(plans);
    }
}
