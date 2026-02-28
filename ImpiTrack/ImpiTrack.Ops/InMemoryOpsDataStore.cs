using System.Collections.Concurrent;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Ops;

/// <summary>
/// Implementacion en memoria de <see cref="IOpsDataStore"/> para bootstrap operativo.
/// </summary>
public sealed class InMemoryOpsDataStore : IOpsDataStore
{
    private readonly ConcurrentDictionary<Guid, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<Guid, RawPacketRecord> _rawById = new();
    private readonly ConcurrentQueue<RawPacketRecord> _rawOrdered = new();
    private readonly ConcurrentDictionary<int, PortCounters> _portCounters = new();
    private readonly int _maxRawPackets;

    /// <summary>
    /// Crea un almacenamiento en memoria acotado por cantidad de paquetes raw.
    /// </summary>
    /// <param name="maxRawPackets">Cantidad maxima de paquetes a retener en memoria.</param>
    public InMemoryOpsDataStore(int maxRawPackets = 50_000)
    {
        _maxRawPackets = Math.Max(1_000, maxRawPackets);
    }

    /// <inheritdoc />
    public void UpsertSession(SessionRecord session)
    {
        _sessions[session.SessionId.Value] = session;
    }

    /// <inheritdoc />
    public void AddRawPacket(RawPacketRecord record, long backlog)
    {
        _rawById[record.PacketId.Value] = record;
        _rawOrdered.Enqueue(record);
        TrimRawIfNeeded();

        PortCounters counters = _portCounters.GetOrAdd(record.Port, static _ => new PortCounters());
        counters.FramesIn = Interlocked.Increment(ref counters.FramesIn);
        if (record.ParseStatus == RawParseStatus.Ok)
        {
            counters.ParseOk = Interlocked.Increment(ref counters.ParseOk);
        }
        else
        {
            counters.ParseFail = Interlocked.Increment(ref counters.ParseFail);
        }

        if (record.AckSent)
        {
            counters.AckSent = Interlocked.Increment(ref counters.AckSent);
        }

        Interlocked.Exchange(ref counters.Backlog, backlog);
    }

    /// <inheritdoc />
    public IReadOnlyList<RawPacketRecord> GetLatestRawPackets(string? imei, int limit)
    {
        int normalizedLimit = Math.Clamp(limit, 1, 500);
        IEnumerable<RawPacketRecord> query = _rawOrdered.Reverse();
        if (!string.IsNullOrWhiteSpace(imei))
        {
            query = query.Where(x => string.Equals(x.Imei, imei, StringComparison.OrdinalIgnoreCase));
        }

        return query.Take(normalizedLimit).ToArray();
    }

    /// <inheritdoc />
    public bool TryGetRawPacket(PacketId packetId, out RawPacketRecord? record)
    {
        bool found = _rawById.TryGetValue(packetId.Value, out RawPacketRecord? value);
        record = value;
        return found;
    }

    /// <inheritdoc />
    public IReadOnlyList<ErrorAggregate> GetTopErrors(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string groupBy,
        int limit)
    {
        int normalizedLimit = Math.Clamp(limit, 1, 200);
        string normalizedGroupBy = NormalizeGroupBy(groupBy);

        return _rawOrdered
            .Where(x =>
                x.ParseStatus != RawParseStatus.Ok &&
                x.ReceivedAtUtc >= fromUtc &&
                x.ReceivedAtUtc <= toUtc)
            .GroupBy(x => BuildErrorGroupKey(x, normalizedGroupBy))
            .Select(group => new ErrorAggregate(
                group.Key,
                group.LongCount(),
                group.OrderByDescending(x => x.ReceivedAtUtc).FirstOrDefault()?.PacketId))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.GroupKey, StringComparer.Ordinal)
            .Take(normalizedLimit)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<SessionRecord> GetActiveSessions(int? port)
    {
        IEnumerable<SessionRecord> query = _sessions.Values.Where(x => x.IsActive);
        if (port.HasValue)
        {
            query = query.Where(x => x.Port == port.Value);
        }

        return query
            .OrderByDescending(x => x.LastSeenAtUtc)
            .ThenBy(x => x.SessionId.Value)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<PortIngestionSnapshot> GetPortSnapshots()
    {
        HashSet<int> ports = _portCounters.Keys.ToHashSet();
        foreach (int sessionPort in _sessions.Values.Select(x => x.Port))
        {
            ports.Add(sessionPort);
        }

        List<PortIngestionSnapshot> snapshots = new(ports.Count);
        foreach (int port in ports.OrderBy(x => x))
        {
            _portCounters.TryGetValue(port, out PortCounters? counters);
            int activeConnections = _sessions.Values.Count(x => x.IsActive && x.Port == port);

            snapshots.Add(new PortIngestionSnapshot(
                port,
                activeConnections,
                counters is null ? 0 : Interlocked.Read(ref counters.FramesIn),
                counters is null ? 0 : Interlocked.Read(ref counters.ParseOk),
                counters is null ? 0 : Interlocked.Read(ref counters.ParseFail),
                counters is null ? 0 : Interlocked.Read(ref counters.AckSent),
                counters is null ? 0 : Interlocked.Read(ref counters.Backlog)));
        }

        return snapshots;
    }

    private static string NormalizeGroupBy(string groupBy)
    {
        if (string.Equals(groupBy, "protocol", StringComparison.OrdinalIgnoreCase))
        {
            return "protocol";
        }

        if (string.Equals(groupBy, "port", StringComparison.OrdinalIgnoreCase))
        {
            return "port";
        }

        return "errorCode";
    }

    private static string BuildErrorGroupKey(RawPacketRecord record, string groupBy)
    {
        return groupBy switch
        {
            "protocol" => record.Protocol.ToString(),
            "port" => record.Port.ToString(),
            _ => record.ParseError ?? "unknown_error"
        };
    }

    private void TrimRawIfNeeded()
    {
        while (_rawOrdered.Count > _maxRawPackets &&
               _rawOrdered.TryDequeue(out RawPacketRecord? removed))
        {
            _rawById.TryRemove(removed.PacketId.Value, out _);
        }
    }

    private sealed class PortCounters
    {
        public long FramesIn;
        public long ParseOk;
        public long ParseFail;
        public long AckSent;
        public long Backlog;
    }
}
