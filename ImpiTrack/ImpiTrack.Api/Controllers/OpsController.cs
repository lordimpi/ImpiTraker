using ImpiTrack.Api.Http;
using ImpiTrack.Application.Abstractions;
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
    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100];

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
    /// Obtiene paquetes raw recientes por IMEI opcional, paginados.
    /// </summary>
    /// <param name="imei">IMEI opcional para filtrar.</param>
    /// <param name="page">Numero de pagina (base 1).</param>
    /// <param name="pageSize">Tamano de pagina (10, 20, 50 o 100).</param>
    /// <param name="limit">Alias obsoleto de pageSize para compatibilidad hacia atras.</param>
    /// <returns>Resultado paginado de paquetes raw recientes.</returns>
    [HttpGet("raw/latest")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RawPacketRecord>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<RawPacketRecord>>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<RawPacketRecord>>>> GetLatestRaw(
        [FromQuery] string? imei,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] int limit = 0)
    {
        // limit alias: backward-compat — treat as pageSize on page 1
        if (pageSize == 0 && limit > 0)
        {
            pageSize = limit;
            page = 1;
        }

        if (pageSize == 0)
        {
            pageSize = 20;
        }

        if (!AllowedPageSizes.Contains(pageSize))
        {
            return this.FailEnvelope<PagedResult<RawPacketRecord>>(
                StatusCodes.Status400BadRequest,
                "invalid_page_size",
                $"El tamano de pagina debe ser uno de: {string.Join(", ", AllowedPageSizes)}.");
        }

        PagedResult<RawPacketRecord> result = await _opsRepository.GetLatestRawPacketsAsync(
            new OpsRawListQuery(page, pageSize, imei),
            HttpContext.RequestAborted);

        return this.OkEnvelope(result);
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
    /// Obtiene sesiones activas paginadas, opcionalmente filtradas por puerto.
    /// </summary>
    /// <param name="port">Puerto opcional de filtrado.</param>
    /// <param name="page">Numero de pagina (base 1).</param>
    /// <param name="pageSize">Tamano de pagina (10, 20, 50 o 100).</param>
    /// <returns>Resultado paginado de sesiones activas.</returns>
    [HttpGet("sessions/active")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SessionRecord>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SessionRecord>>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PagedResult<SessionRecord>>>> GetActiveSessions(
        [FromQuery] int? port,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!AllowedPageSizes.Contains(pageSize))
        {
            return this.FailEnvelope<PagedResult<SessionRecord>>(
                StatusCodes.Status400BadRequest,
                "invalid_page_size",
                $"El tamano de pagina debe ser uno de: {string.Join(", ", AllowedPageSizes)}.");
        }

        PagedResult<SessionRecord> result = await _opsRepository.GetActiveSessionsAsync(
            new OpsSessionListQuery(page, pageSize, port),
            HttpContext.RequestAborted);

        return this.OkEnvelope(result);
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
