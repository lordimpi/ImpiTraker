using System.Collections.Concurrent;
using ImpiTrack.Tcp.Core.Configuration;

namespace ImpiTrack.Tcp.Core.Security;

/// <summary>
/// Implementacion en memoria de <see cref="IAbuseGuard"/> con ventanas moviles de un minuto.
/// </summary>
public sealed class InMemoryAbuseGuard : IAbuseGuard
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, AbuseState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxFramesPerMinute;
    private readonly int _invalidFrameThreshold;
    private readonly TimeSpan _banDuration;

    /// <summary>
    /// Crea un guard de abuso con opciones de seguridad TCP.
    /// </summary>
    /// <param name="options">Opciones de seguridad a aplicar.</param>
    public InMemoryAbuseGuard(TcpSecurityOptions options)
    {
        _maxFramesPerMinute = Math.Max(1, options.MaxFramesPerMinutePerIp);
        _invalidFrameThreshold = Math.Max(1, options.InvalidFrameThreshold);
        _banDuration = TimeSpan.FromMinutes(Math.Max(1, options.BanMinutes));
    }

    /// <inheritdoc />
    public bool IsBlocked(string remoteIp, DateTimeOffset nowUtc, out DateTimeOffset? blockedUntilUtc)
    {
        blockedUntilUtc = null;
        if (!_states.TryGetValue(remoteIp, out AbuseState? state))
        {
            return false;
        }

        lock (state.Sync)
        {
            TrimOld(state.Frames, nowUtc);
            TrimOld(state.InvalidFrames, nowUtc);

            if (state.BlockedUntilUtc.HasValue && state.BlockedUntilUtc.Value > nowUtc)
            {
                blockedUntilUtc = state.BlockedUntilUtc;
                return true;
            }

            if (state.BlockedUntilUtc.HasValue && state.BlockedUntilUtc.Value <= nowUtc)
            {
                state.BlockedUntilUtc = null;
            }

            return false;
        }
    }

    /// <inheritdoc />
    public void RegisterFrame(string remoteIp, bool isInvalid, DateTimeOffset nowUtc)
    {
        AbuseState state = _states.GetOrAdd(remoteIp, static _ => new AbuseState());
        lock (state.Sync)
        {
            if (state.BlockedUntilUtc.HasValue && state.BlockedUntilUtc.Value > nowUtc)
            {
                return;
            }

            state.Frames.Enqueue(nowUtc);
            TrimOld(state.Frames, nowUtc);

            if (isInvalid)
            {
                state.InvalidFrames.Enqueue(nowUtc);
                TrimOld(state.InvalidFrames, nowUtc);
            }

            if (state.Frames.Count > _maxFramesPerMinute ||
                state.InvalidFrames.Count > _invalidFrameThreshold)
            {
                state.BlockedUntilUtc = nowUtc.Add(_banDuration);
            }
        }
    }

    private static void TrimOld(Queue<DateTimeOffset> queue, DateTimeOffset nowUtc)
    {
        while (queue.Count > 0 && nowUtc - queue.Peek() > OneMinute)
        {
            queue.Dequeue();
        }
    }

    private sealed class AbuseState
    {
        public object Sync { get; } = new();
        public Queue<DateTimeOffset> Frames { get; } = new();
        public Queue<DateTimeOffset> InvalidFrames { get; } = new();
        public DateTimeOffset? BlockedUntilUtc { get; set; }
    }
}
