using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
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
    private readonly IOpsDataStore _opsDataStore;

    /// <summary>
    /// Crea un controlador de diagnostico operativo.
    /// </summary>
    /// <param name="opsDataStore">Almacen de datos operativos.</param>
    public OpsController(IOpsDataStore opsDataStore)
    {
        _opsDataStore = opsDataStore;
    }

    /// <summary>
    /// Obtiene paquetes raw recientes por IMEI opcional.
    /// </summary>
    /// <param name="imei">IMEI opcional para filtrar.</param>
    /// <param name="limit">Limite maximo de resultados.</param>
    /// <returns>Lista de paquetes raw recientes.</returns>
    [HttpGet("raw/latest")]
    [ProducesResponseType(typeof(IReadOnlyList<RawPacketRecord>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<RawPacketRecord>> GetLatestRaw(
        [FromQuery] string? imei,
        [FromQuery] int limit = 50)
    {
        return Ok(_opsDataStore.GetLatestRawPackets(imei, limit));
    }

    /// <summary>
    /// Obtiene un paquete raw puntual por id.
    /// </summary>
    /// <param name="packetId">Id de paquete a consultar.</param>
    /// <returns>Paquete raw encontrado o 404.</returns>
    [HttpGet("raw/{packetId:guid}")]
    [ProducesResponseType(typeof(RawPacketRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RawPacketRecord> GetRawByPacketId([FromRoute] Guid packetId)
    {
        bool found = _opsDataStore.TryGetRawPacket(new PacketId(packetId), out RawPacketRecord? record);
        if (!found || record is null)
        {
            return NotFound();
        }

        return Ok(record);
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
    [ProducesResponseType(typeof(IReadOnlyList<ErrorAggregate>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<ErrorAggregate>> GetTopErrors(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string groupBy = "errorCode",
        [FromQuery] int limit = 20)
    {
        DateTimeOffset toUtc = to ?? DateTimeOffset.UtcNow;
        DateTimeOffset fromUtc = from ?? toUtc.AddHours(-1);
        return Ok(_opsDataStore.GetTopErrors(fromUtc, toUtc, groupBy, limit));
    }

    /// <summary>
    /// Obtiene sesiones activas opcionalmente filtradas por puerto.
    /// </summary>
    /// <param name="port">Puerto opcional de filtrado.</param>
    /// <returns>Sesiones activas.</returns>
    [HttpGet("sessions/active")]
    [ProducesResponseType(typeof(IReadOnlyList<SessionRecord>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SessionRecord>> GetActiveSessions([FromQuery] int? port)
    {
        return Ok(_opsDataStore.GetActiveSessions(port));
    }

    /// <summary>
    /// Obtiene resumen de ingesta por puerto.
    /// </summary>
    /// <returns>Snapshots operativos por puerto.</returns>
    [HttpGet("ingestion/ports")]
    [ProducesResponseType(typeof(IReadOnlyList<PortIngestionSnapshot>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PortIngestionSnapshot>> GetIngestionPorts()
    {
        return Ok(_opsDataStore.GetPortSnapshots());
    }
}
