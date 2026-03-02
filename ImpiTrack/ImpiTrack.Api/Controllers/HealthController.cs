using ImpiTrack.Api.Http;
using ImpiTrack.Shared.Api;
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
    public ActionResult<ApiResponse<HealthStatusResponse>> Health()
    {
        return this.OkEnvelope(new HealthStatusResponse(
            "ok",
            "ImpiTrack.Api",
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Verifica readiness de la API para recibir trafico.
    /// </summary>
    /// <returns>Estado de readiness del servicio.</returns>
    [HttpGet("/ready")]
    public ActionResult<ApiResponse<HealthStatusResponse>> Ready()
    {
        return this.OkEnvelope(new HealthStatusResponse(
            "ready",
            "ImpiTrack.Api",
            DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Contrato de estado de salud para endpoints de disponibilidad.
    /// </summary>
    /// <param name="Status">Estado funcional del servicio.</param>
    /// <param name="Service">Nombre del servicio.</param>
    /// <param name="Utc">Fecha UTC del muestreo.</param>
    public sealed record HealthStatusResponse(
        string Status,
        string Service,
        DateTimeOffset Utc);
}
