using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.DataAccess.Abstractions;

/// <summary>
/// Consultas operativas consumidas por la API de diagnostico.
/// </summary>
public interface IOpsRepository
{
    /// <summary>
    /// Obtiene paquetes raw recientes filtrando por IMEI opcional.
    /// </summary>
    /// <param name="imei">IMEI opcional para filtrar.</param>
    /// <param name="limit">Cantidad maxima de resultados.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Lista de paquetes raw recientes.</returns>
    Task<IReadOnlyList<RawPacketRecord>> GetLatestRawPacketsAsync(
        string? imei,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene un paquete raw puntual por identificador.
    /// </summary>
    /// <param name="packetId">Identificador de paquete.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Registro encontrado o <c>null</c>.</returns>
    Task<RawPacketRecord?> GetRawPacketByIdAsync(PacketId packetId, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene agregados de errores de parseo en una ventana temporal.
    /// </summary>
    /// <param name="fromUtc">Inicio UTC del rango.</param>
    /// <param name="toUtc">Fin UTC del rango.</param>
    /// <param name="groupBy">Criterio de agrupacion: protocol, port o errorCode.</param>
    /// <param name="limit">Cantidad maxima de grupos.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Agregados ordenados por ocurrencias.</returns>
    Task<IReadOnlyList<ErrorAggregate>> GetTopErrorsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string groupBy,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene sesiones activas filtradas opcionalmente por puerto.
    /// </summary>
    /// <param name="port">Puerto opcional para filtrar.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Lista de sesiones activas.</returns>
    Task<IReadOnlyList<SessionRecord>> GetActiveSessionsAsync(int? port, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene snapshots agregados de ingesta por puerto.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Lista de snapshots por puerto.</returns>
    Task<IReadOnlyList<PortIngestionSnapshot>> GetPortSnapshotsAsync(CancellationToken cancellationToken);
}
