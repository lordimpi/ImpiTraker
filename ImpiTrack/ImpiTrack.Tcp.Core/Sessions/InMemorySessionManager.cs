using System.Collections.Concurrent;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Sessions;

/// <summary>
/// Implementacion en memoria y segura para hilos de <see cref="ISessionManager"/>.
/// </summary>
public sealed class InMemorySessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();

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
            LastSeenAtUtc = DateTimeOffset.UtcNow
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

        if (_sessions.TryGetValue(sessionId.Value, out SessionState? session))
        {
            session.Imei = imei;
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
        return _sessions.TryRemove(sessionId.Value, out _);
    }
}
