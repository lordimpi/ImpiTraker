using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints basicos de salud de la Web API.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    /// <summary>
    /// Verifica liveness del proceso API.
    /// </summary>
    /// <returns>Estado saludable del servicio.</returns>
    [HttpGet("/health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "ok",
            service = "ImpiTrack.Api",
            utc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Verifica readiness de la API para recibir trafico.
    /// </summary>
    /// <returns>Estado de readiness del servicio.</returns>
    [HttpGet("/ready")]
    public IActionResult Ready()
    {
        return Ok(new
        {
            status = "ready",
            service = "ImpiTrack.Api",
            utc = DateTimeOffset.UtcNow
        });
    }
}
