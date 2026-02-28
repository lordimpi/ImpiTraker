using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.Security;

namespace ImpiTrack.Tests;

public sealed class AbuseGuardTests
{
    [Fact]
    public void IsBlocked_ShouldBlockWhenFramesPerMinuteExceeded()
    {
        var options = new TcpSecurityOptions
        {
            MaxFramesPerMinutePerIp = 2,
            InvalidFrameThreshold = 10,
            BanMinutes = 5
        };
        var guard = new InMemoryAbuseGuard(options);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        guard.RegisterFrame("10.0.0.1", isInvalid: false, now);
        guard.RegisterFrame("10.0.0.1", isInvalid: false, now.AddSeconds(1));
        guard.RegisterFrame("10.0.0.1", isInvalid: false, now.AddSeconds(2));

        bool blocked = guard.IsBlocked("10.0.0.1", now.AddSeconds(3), out DateTimeOffset? blockedUntil);

        Assert.True(blocked);
        Assert.NotNull(blockedUntil);
        Assert.True(blockedUntil > now);
    }

    [Fact]
    public void IsBlocked_ShouldBlockWhenInvalidThresholdExceeded()
    {
        var options = new TcpSecurityOptions
        {
            MaxFramesPerMinutePerIp = 100,
            InvalidFrameThreshold = 1,
            BanMinutes = 2
        };
        var guard = new InMemoryAbuseGuard(options);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        guard.RegisterFrame("10.0.0.2", isInvalid: true, now);
        guard.RegisterFrame("10.0.0.2", isInvalid: true, now.AddSeconds(1));

        bool blocked = guard.IsBlocked("10.0.0.2", now.AddSeconds(1), out _);
        Assert.True(blocked);
    }
}
