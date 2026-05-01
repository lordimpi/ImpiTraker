using System.Collections.Concurrent;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Sessions;

/// <summary>
/// Implementacion en memoria y segura para hilos de <see cref="ISessionManager"/>.
/// </summary>
public sealed class InMemorySessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();
    private readonly ConcurrentDictionary<string, Guid> _imeiIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event Action<SessionState>? ImeiAttached;

    /// <inheritdoc />
    public SessionState Open(string remoteIp, int port)
    {
        SessionId sessionId = SessionId.New();
        SessionState state = new()
        {
            SessionId = sessionId,
            RemoteIp = remoteIp,
            Port = port,
            ConnectedAtUtc = DateTimeOffset.UtcNow,
            LastSeenAtUtc = DateTimeOffset.UtcNow,
            FramesIn = 0,
            FramesInvalid = 0
        };

        _sessions[sessionId.Value] = state;
        return state;
    }

    /// <inheritdoc />
    public void Touch(SessionId sessionId)
    {
        if (_sessions.TryGetValue(sessionId.Value, out SessionState? session))
        {
            session.LastSeenAtUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <inheritdoc />
    public void AttachImei(SessionId sessionId, string? imei)
    {
        if (string.IsNullOrWhiteSpace(imei))
        {
            return;
        }

        if (!_sessions.TryGetValue(sessionId.Value, out SessionState? session))
        {
            return;
        }

        session.Imei = imei;
        _imeiIndex[imei] = sessionId.Value;

        try
        {
            ImeiAttached?.Invoke(session);
        }
        catch
        {
            // Best-effort: never let a buggy subscriber kill AttachImei.
        }
    }

    /// <inheritdoc />
    public void MarkHeartbeat(SessionId sessionId)
    {
        if (_sessions.TryGetValue(sessionId.Value, out SessionState? session))
        {
            session.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <inheritdoc />
    public void IncrementFramesIn(SessionId sessionId)
    {
        if (_sessions.TryGetValue(sessionId.Value, out SessionState? session))
        {
            session.FramesIn++;
        }
    }

    /// <inheritdoc />
    public void IncrementFramesInvalid(SessionId sessionId)
    {
        if (_sessions.TryGetValue(sessionId.Value, out SessionState? session))
        {
            session.FramesInvalid++;
        }
    }

    /// <inheritdoc />
    public void SetCloseReason(SessionId sessionId, string closeReason)
    {
        if (_sessions.TryGetValue(sessionId.Value, out SessionState? session))
        {
            session.CloseReason = closeReason;
        }
    }

    /// <inheritdoc />
    public bool TryGet(SessionId sessionId, out SessionState? session)
    {
        return _sessions.TryGetValue(sessionId.Value, out session);
    }

    /// <inheritdoc />
    public bool Close(SessionId sessionId)
    {
        if (!_sessions.TryRemove(sessionId.Value, out SessionState? session))
        {
            return false;
        }

        session.OutboundChannel.Writer.TryComplete();

        if (session.Imei is not null)
        {
            _imeiIndex.TryRemove(session.Imei, out _);
        }

        session.DisconnectedAtUtc = DateTimeOffset.UtcNow;
        return true;
    }

    /// <inheritdoc />
    public bool TryGetByImei(string imei, out SessionState? session)
    {
        session = null;
        return _imeiIndex.TryGetValue(imei, out Guid id)
            && _sessions.TryGetValue(id, out session);
    }

    /// <inheritdoc />
    public bool TryEnqueueCommand(string imei, OutboundFrame frame)
    {
        if (!TryGetByImei(imei, out SessionState? session) || session is null)
        {
            return false;
        }

        if (session.DisconnectedAtUtc is not null || !session.IsCommandable)
        {
            return false;
        }

        return session.OutboundChannel.Writer.TryWrite(frame);
    }
}
