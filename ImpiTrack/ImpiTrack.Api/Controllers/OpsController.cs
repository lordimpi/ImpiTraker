using ImpiTrack.Api.Http;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Shared.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Endpoints operativos de diagnostico para ingestion TCP.
/// </summary>
[ApiController]
[Route("api/ops")]
[Authorize(Policy = "Admin")]
public sealed class OpsController : ControllerBase
{
    private readonly IOpsRepository _opsRepository;

    /// <summary>
    /// Crea un controlador de diagnostico operativo.
    /// </summary>
    /// <param name="opsRepository">Repositorio de consultas operativas.</param>
    public OpsController(IOpsRepository opsRepository)
    {
        _opsRepository = opsRepository;
    }

    /// <summary>
    /// Obtiene paquetes raw recientes por IMEI opcional.
    /// </summary>
    /// <param name="imei">IMEI opcional para filtrar.</param>
    /// <param name="limit">Limite maximo de resultados.</param>
    /// <returns>Lista de paquetes raw recientes.</returns>
    [HttpGet("raw/latest")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<RawPacketRecord>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RawPacketRecord>>>> GetLatestRaw(
        [FromQuery] string? imei,
        [FromQuery] int limit = 50)
    {
        IReadOnlyList<RawPacketRecord> records = await _opsRepository.GetLatestRawPacketsAsync(
            imei,
            limit,
            HttpContext.RequestAborted);

        return this.OkEnvelope(records);
    }

    /// <summary>
    /// Obtiene un paquete raw puntual por id.
    /// </summary>
    /// <param name="packetId">Id de paquete a consultar.</param>
    /// <returns>Paquete raw encontrado o 404.</returns>
    [HttpGet("raw/{packetId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<RawPacketRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RawPacketRecord>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<RawPacketRecord>>> GetRawByPacketId([FromRoute] Guid packetId)
    {
        RawPacketRecord? record = await _opsRepository.GetRawPacketByIdAsync(
            new PacketId(packetId),
            HttpContext.RequestAborted);

        if (record is null)
        {
            return this.FailEnvelope<RawPacketRecord>(
                StatusCodes.Status404NotFound,
                "resource_not_found",
                "No existe un paquete para el identificador solicitado.");
        }

        return this.OkEnvelope(record);
    }

    /// <summary>
    /// Obtiene top de errores de parseo en una ventana temporal.
    /// </summary>
    /// <param name="from">Inicio UTC de la ventana.</param>
    /// <param name="to">Fin UTC de la ventana.</param>
    /// <param name="groupBy">Criterio de agrupacion: protocol, port o errorCode.</param>
    /// <param name="limit">Limite de grupos devueltos.</param>
    /// <returns>Agregados de errores.</returns>
    [HttpGet("errors/top")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ErrorAggregate>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ErrorAggregate>>>> GetTopErrors(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string groupBy = "errorCode",
        [FromQuery] int limit = 20)
    {
        DateTimeOffset toUtc = to ?? DateTimeOffset.UtcNow;
        DateTimeOffset fromUtc = from ?? toUtc.AddHours(-1);
        IReadOnlyList<ErrorAggregate> response = await _opsRepository.GetTopErrorsAsync(
            fromUtc,
            toUtc,
            groupBy,
            limit,
            HttpContext.RequestAborted);

        return this.OkEnvelope(response);
    }

    /// <summary>
    /// Obtiene sesiones activas opcionalmente filtradas por puerto.
    /// </summary>
    /// <param name="port">Puerto opcional de filtrado.</param>
    /// <returns>Sesiones activas.</returns>
    [HttpGet("sessions/active")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SessionRecord>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SessionRecord>>>> GetActiveSessions([FromQuery] int? port)
    {
        IReadOnlyList<SessionRecord> sessions = await _opsRepository.GetActiveSessionsAsync(
            port,
            HttpContext.RequestAborted);

        return this.OkEnvelope(sessions);
    }

    /// <summary>
    /// Obtiene resumen de ingesta por puerto.
    /// </summary>
    /// <returns>Snapshots operativos por puerto.</returns>
    [HttpGet("ingestion/ports")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PortIngestionSnapshot>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PortIngestionSnapshot>>>> GetIngestionPorts()
    {
        IReadOnlyList<PortIngestionSnapshot> snapshots = await _opsRepository.GetPortSnapshotsAsync(HttpContext.RequestAborted);
        return this.OkEnvelope(snapshots);
    }
}
