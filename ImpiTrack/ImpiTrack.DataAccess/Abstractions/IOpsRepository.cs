using ImpiTrack.Application.Abstractions;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.DataAccess.Abstractions;

/// <summary>
/// Consultas operativas consumidas por la API de diagnostico.
/// </summary>
public interface IOpsRepository
{
    /// <summary>
    /// Obtiene paquetes raw recientes de forma paginada, filtrando por IMEI opcional.
    /// </summary>
    /// <param name="query">Parametros de paginacion y filtrado.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado paginado de paquetes raw recientes.</returns>
    Task<PagedResult<RawPacketRecord>> GetLatestRawPacketsAsync(
        OpsRawListQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene un paquete raw puntual por identificador.
    /// </summary>
    /// <param name="packetId">Identificador de paquete.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Registro encontrado o <c>null</c>.</returns>
    Task<RawPacketRecord?> GetRawPacketByIdAsync(PacketId packetId, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene agregados de errores de parseo en una ventana temporal, paginados.
    /// </summary>
    /// <param name="query">Parametros de paginacion, rango temporal y agrupacion.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado paginado de agregados ordenados por ocurrencias.</returns>
    Task<PagedResult<ErrorAggregate>> GetTopErrorsAsync(OpsErrorListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene sesiones activas de forma paginada, filtradas opcionalmente por puerto.
    /// </summary>
    /// <param name="query">Parametros de paginacion y filtrado.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Resultado paginado de sesiones activas.</returns>
    Task<PagedResult<SessionRecord>> GetActiveSessionsAsync(OpsSessionListQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene snapshots agregados de ingesta por puerto.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Lista de snapshots por puerto.</returns>
    Task<IReadOnlyList<PortIngestionSnapshot>> GetPortSnapshotsAsync(CancellationToken cancellationToken);
}
