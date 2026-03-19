using ImpiTrack.Api.Services;
using ImpiTrack.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImpiTrack.Tests;

/// <summary>
/// Tests unitarios para CachedDeviceOwnershipResolver.
/// E.2: Verifica que la primera llamada consulta al repositorio y la segunda usa cache dentro del TTL.
/// </summary>
public sealed class CachedDeviceOwnershipResolverTests
{
    [Fact]
    public async Task FirstCall_HitsRepository()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        var repo = new CountingTelemetryRepository(
            new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["864035053671278"] = [userId]
            });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new CachedDeviceOwnershipResolver(
            repo, cache, NullLogger<CachedDeviceOwnershipResolver>.Instance);

        // Act
        IReadOnlyList<Guid> result = await resolver.GetUserIdsForImeiAsync("864035053671278", CancellationToken.None);

        // Assert
        Assert.Single(result);
        Assert.Equal(userId, result[0]);
        Assert.Equal(1, repo.CallCount);
    }

    [Fact]
    public async Task SecondCallWithinTtl_UsesCacheDoesNotHitRepository()
    {
        // Arrange
        Guid userId = Guid.NewGuid();
        var repo = new CountingTelemetryRepository(
            new Dictionary<string, IReadOnlyList<Guid>>
            {
                ["864035053671278"] = [userId]
            });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new CachedDeviceOwnershipResolver(
            repo, cache, NullLogger<CachedDeviceOwnershipResolver>.Instance);

        // Act — two calls
        await resolver.GetUserIdsForImeiAsync("864035053671278", CancellationToken.None);
        IReadOnlyList<Guid> second = await resolver.GetUserIdsForImeiAsync("864035053671278", CancellationToken.None);

        // Assert — repo called only once
        Assert.Single(second);
        Assert.Equal(userId, second[0]);
        Assert.Equal(1, repo.CallCount);
    }

    [Fact]
    public async Task UnboundImei_ReturnsEmptyList()
    {
        // Arrange
        var repo = new CountingTelemetryRepository(new Dictionary<string, IReadOnlyList<Guid>>());
        var cache = new MemoryCache(new MemoryCacheOptions());
        var resolver = new CachedDeviceOwnershipResolver(
            repo, cache, NullLogger<CachedDeviceOwnershipResolver>.Instance);

        // Act
        IReadOnlyList<Guid> result = await resolver.GetUserIdsForImeiAsync("999999999999999", CancellationToken.None);

        // Assert
        Assert.Empty(result);
        Assert.Equal(1, repo.CallCount);
    }

    // ──── Stubs ───────────────────────────────────────────────────────────────

    private sealed class CountingTelemetryRepository : ITelemetryQueryRepository
    {
        private readonly Dictionary<string, IReadOnlyList<Guid>> _map;
        public int CallCount { get; private set; }

        public CountingTelemetryRepository(Dictionary<string, IReadOnlyList<Guid>> map) => _map = map;

        public Task<IReadOnlyList<Guid>> GetActiveUserIdsByImeiAsync(string imei, CancellationToken cancellationToken)
        {
            CallCount++;
            IReadOnlyList<Guid> result = _map.TryGetValue(imei.Trim(), out IReadOnlyList<Guid>? ids)
                ? ids
                : [];
            return Task.FromResult(result);
        }

        // ── Unused interface members ─────────────────────────────────────────
        public Task<IReadOnlyList<TelemetryDeviceSummaryDto>> GetDeviceSummariesAsync(Guid userId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<TelemetryDeviceSummaryDto>>([]);

        public Task<bool> HasActiveDeviceBindingAsync(Guid userId, string imei, CancellationToken ct)
            => Task.FromResult(false);

        public Task<IReadOnlyList<DevicePositionPointDto>> GetPositionsAsync(Guid userId, string imei, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DevicePositionPointDto>>([]);

        public Task<IReadOnlyList<DeviceEventDto>> GetEventsAsync(Guid userId, string imei, DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);

        public Task<IReadOnlyList<DevicePositionPointDto>> GetTripCandidatePositionsAsync(Guid userId, string imei, DateTimeOffset from, DateTimeOffset to, int maxPoints, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DevicePositionPointDto>>([]);

        public Task<IReadOnlyList<AccEventDto>> GetAccEventsForWindowAsync(string imei, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AccEventDto>>([]);
    }
}
