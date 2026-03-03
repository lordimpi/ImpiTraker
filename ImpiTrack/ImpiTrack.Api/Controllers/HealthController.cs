using ImpiTrack.Api.Http;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Connection;
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
    private readonly DatabaseRuntimeContext _databaseRuntimeContext;
    private readonly ILogger<HealthController> _logger;

    /// <summary>
    /// Crea un controlador de salud para liveness/readiness de la API.
    /// </summary>
    /// <param name="databaseRuntimeContext">Contexto de runtime de persistencia.</param>
    /// <param name="logger">Logger del controlador.</param>
    public HealthController(DatabaseRuntimeContext databaseRuntimeContext, ILogger<HealthController> logger)
    {
        _databaseRuntimeContext = databaseRuntimeContext;
        _logger = logger;
    }

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
    /// <param name="cancellationToken">Token de cancelacion de la solicitud.</param>
    /// <returns>Estado de readiness del servicio.</returns>
    [HttpGet("/ready")]
    public async Task<ActionResult<ApiResponse<HealthStatusResponse>>> Ready(CancellationToken cancellationToken)
    {
        if (_databaseRuntimeContext.IsSqlEnabled)
        {
            IDbConnectionFactory? connectionFactory = HttpContext.RequestServices.GetService<IDbConnectionFactory>();
            if (connectionFactory is null)
            {
                _logger.LogError(
                    "health_ready_storage_factory_missing provider={provider}",
                    _databaseRuntimeContext.Provider);

                return this.FailEnvelope<HealthStatusResponse>(
                    StatusCodes.Status503ServiceUnavailable,
                    "storage_unavailable",
                    "La API no esta lista porque la base de datos no esta disponible.");
            }

            try
            {
                await using var connection = await connectionFactory.CreateOpenConnectionAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                command.CommandTimeout = _databaseRuntimeContext.CommandTimeoutSeconds;
                await command.ExecuteScalarAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "health_ready_storage_check_failed provider={provider}",
                    _databaseRuntimeContext.Provider);

                return this.FailEnvelope<HealthStatusResponse>(
                    StatusCodes.Status503ServiceUnavailable,
                    "storage_unavailable",
                    "La API no esta lista porque la base de datos no esta disponible.");
            }
        }

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
