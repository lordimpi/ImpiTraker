using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Ops;

/// <summary>
/// Contrato de almacenamiento operativo para datos de diagnostico del pipeline TCP.
/// </summary>
public interface IOpsDataStore
{
    /// <summary>
    /// Actualiza o inserta el snapshot de una sesion.
    /// </summary>
    /// <param name="session">Snapshot a persistir.</param>
    void UpsertSession(SessionRecord session);

    /// <summary>
    /// Inserta un paquete raw en el historico de evidencia.
    /// </summary>
    /// <param name="record">Registro raw a guardar.</param>
    /// <param name="backlog">Backlog observado al momento de guardar.</param>
    void AddRawPacket(RawPacketRecord record, long backlog);

    /// <summary>
    /// Obtiene paquetes raw recientes filtrados por IMEI opcional.
    /// </summary>
    /// <param name="imei">IMEI opcional para filtrar resultados.</param>
    /// <param name="limit">Cantidad maxima de registros a devolver.</param>
    /// <returns>Lista de paquetes en orden descendente por fecha de recepcion.</returns>
    IReadOnlyList<RawPacketRecord> GetLatestRawPackets(string? imei, int limit);

    /// <summary>
    /// Obtiene un paquete raw puntual por id de paquete.
    /// </summary>
    /// <param name="packetId">Id del paquete buscado.</param>
    /// <param name="record">Registro encontrado cuando existe.</param>
    /// <returns><c>true</c> si el paquete existe; de lo contrario <c>false</c>.</returns>
    bool TryGetRawPacket(PacketId packetId, out RawPacketRecord? record);

    /// <summary>
    /// Obtiene agregados de errores en una ventana de tiempo.
    /// </summary>
    /// <param name="fromUtc">Fecha UTC inicial de la ventana.</param>
    /// <param name="toUtc">Fecha UTC final de la ventana.</param>
    /// <param name="groupBy">Criterio de agrupacion: <c>protocol</c>, <c>port</c> o <c>errorCode</c>.</param>
    /// <param name="limit">Cantidad maxima de grupos a devolver.</param>
    /// <returns>Agregados ordenados por ocurrencias descendentes.</returns>
    IReadOnlyList<ErrorAggregate> GetTopErrors(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string groupBy,
        int limit);

    /// <summary>
    /// Obtiene sesiones activas filtradas por puerto opcional.
    /// </summary>
    /// <param name="port">Puerto opcional para filtrar sesiones activas.</param>
    /// <returns>Lista de sesiones activas.</returns>
    IReadOnlyList<SessionRecord> GetActiveSessions(int? port);

    /// <summary>
    /// Obtiene snapshots agregados de ingesta por puerto.
    /// </summary>
    /// <returns>Lista de snapshots por puerto.</returns>
    IReadOnlyList<PortIngestionSnapshot> GetPortSnapshots();
}
