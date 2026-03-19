using ImpiTrack.Application.Abstractions;
using ImpiTrack.Shared.Options;

namespace ImpiTrack.Tests;

/// <summary>
/// Tests unitarios para DevicePresenceTracker: deteccion de online/offline de dispositivos.
/// Verifica thread-safety implicitamente via ConcurrentDictionary y comportamiento de thresholds.
/// </summary>
public sealed class DevicePresenceTrackerTests
{
    // ─── RecordActivity ─────────────────────────────────────────────────────

    [Fact]
    public void RecordActivity_FirstCall_ReturnsNewlyOnline()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);

        bool result = tracker.RecordActivity("864035053671278");

        Assert.True(result);
    }

    [Fact]
    public void RecordActivity_SecondCallWithinThreshold_ReturnsNotNewlyOnline()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);

        tracker.RecordActivity("864035053671278");
        bool result = tracker.RecordActivity("864035053671278");

        Assert.False(result);
    }

    [Fact]
    public void RecordActivity_DifferentImeis_BothReturnNewlyOnline()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);

        bool first = tracker.RecordActivity("864035053671278");
        bool second = tracker.RecordActivity("999999999999999");

        Assert.True(first);
        Assert.True(second);
    }

    [Fact]
    public void RecordActivity_CaseInsensitiveImei()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);

        bool first = tracker.RecordActivity("ABCDEF123456789");
        bool second = tracker.RecordActivity("abcdef123456789");

        Assert.True(first);
        Assert.False(second);
    }

    // ─── DetectAndRemoveOfflineDevices ───────────────────────────────────────

    [Fact]
    public void DetectAndRemoveOfflineDevices_NoDevices_ReturnsEmpty()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);

        IReadOnlyList<string> offline = tracker.DetectAndRemoveOfflineDevices(TimeSpan.FromMinutes(10));

        Assert.Empty(offline);
    }

    [Fact]
    public void DetectAndRemoveOfflineDevices_RecentActivity_ReturnsEmpty()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);
        tracker.RecordActivity("864035053671278");

        IReadOnlyList<string> offline = tracker.DetectAndRemoveOfflineDevices(TimeSpan.FromMinutes(10));

        Assert.Empty(offline);
    }

    [Fact]
    public void DetectAndRemoveOfflineDevices_ZeroThreshold_DetectsAllAsOffline()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);
        tracker.RecordActivity("864035053671278");
        tracker.RecordActivity("999999999999999");

        // Use zero threshold — everything is "offline" since the timestamp is always in the past (even by nanoseconds)
        // We need to wait at least a tiny bit or use a threshold that makes the cutoff in the future
        IReadOnlyList<string> offline = tracker.DetectAndRemoveOfflineDevices(TimeSpan.Zero);

        Assert.Equal(2, offline.Count);
        Assert.Contains("864035053671278", offline);
        Assert.Contains("999999999999999", offline);
    }

    [Fact]
    public void DetectAndRemoveOfflineDevices_RemovesFromTracking()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);
        tracker.RecordActivity("864035053671278");

        // First scan: force detect with zero threshold
        IReadOnlyList<string> firstScan = tracker.DetectAndRemoveOfflineDevices(TimeSpan.Zero);
        Assert.Single(firstScan);

        // Second scan: should be empty since the device was removed
        IReadOnlyList<string> secondScan = tracker.DetectAndRemoveOfflineDevices(TimeSpan.Zero);
        Assert.Empty(secondScan);
    }

    [Fact]
    public void DetectAndRemoveOfflineDevices_AfterRemoval_RecordActivityReturnsNewlyOnline()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);
        tracker.RecordActivity("864035053671278");

        // Force offline detection
        tracker.DetectAndRemoveOfflineDevices(TimeSpan.Zero);

        // Record again — should be newly online since it was removed
        bool result = tracker.RecordActivity("864035053671278");
        Assert.True(result);
    }

    // ─── Concurrency smoke test ──────────────────────────────────────────────

    [Fact]
    public async Task RecordActivity_ConcurrentCalls_DoesNotThrow()
    {
        var tracker = BuildTracker(offlineThresholdMinutes: 10);
        const int parallelism = 50;

        var tasks = Enumerable.Range(0, parallelism)
            .Select(i => Task.Run(() =>
            {
                string imei = $"IMEI{i:D15}";
                tracker.RecordActivity(imei);
                tracker.RecordActivity(imei);
            }));

        await Task.WhenAll(tasks);

        // No exception means thread-safety is maintained
        IReadOnlyList<string> offline = tracker.DetectAndRemoveOfflineDevices(TimeSpan.Zero);
        Assert.Equal(parallelism, offline.Count);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static DevicePresenceTracker BuildTracker(int offlineThresholdMinutes)
    {
        var options = new StubOptionsService<DevicePresenceOptions>(
            new DevicePresenceOptions { OfflineThresholdMinutes = offlineThresholdMinutes });

        return new DevicePresenceTracker(options);
    }

    private sealed class StubOptionsService<TOptions> : IGenericOptionsService<TOptions>
        where TOptions : class, new()
    {
        private readonly TOptions _value;

        public StubOptionsService(TOptions value) => _value = value;

        public TOptions GetOptions() => _value;
        public TOptions GetSnapshotOptions() => _value;
        public TOptions GetMonitorOptions() => _value;
    }
}
