using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Queue;

namespace ImpiTrack.DataAccess.InMemory;

/// <summary>
/// Repositorio en memoria para pruebas locales cuando no hay base de datos SQL configurada.
/// </summary>
public sealed class InMemoryDataRepository : IOpsRepository, IIngestionRepository
{
    private readonly IOpsDataStore _store;

    /// <summary>
    /// Crea un repositorio en memoria con almacenamiento operativo compartido.
    /// </summary>
    /// <param name="store">Almacen operativo en memoria.</param>
    public InMemoryDataRepository(IOpsDataStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task AddRawPacketAsync(RawPacketRecord record, long backlog, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.AddRawPacket(record, backlog);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertSessionAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.UpsertSession(session);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PersistEnvelopeAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RawPacketRecord>> GetLatestRawPacketsAsync(string? imei, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetLatestRawPackets(imei, limit));
    }

    /// <inheritdoc />
    public Task<RawPacketRecord?> GetRawPacketByIdAsync(PacketId packetId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryGetRawPacket(packetId, out RawPacketRecord? record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ErrorAggregate>> GetTopErrorsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string groupBy,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetTopErrors(fromUtc, toUtc, groupBy, limit));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> GetActiveSessionsAsync(int? port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetActiveSessions(port));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PortIngestionSnapshot>> GetPortSnapshotsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetPortSnapshots());
    }
}
