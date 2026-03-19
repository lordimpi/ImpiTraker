using ImpiTrack.Application.Abstractions;
using Microsoft.Extensions.Caching.Memory;

namespace ImpiTrack.Api.Services;

/// <summary>
/// Resuelve IMEI → userId(s) con cache en memoria para reducir carga en la base de datos.
/// TTL absoluto de 30 segundos por IMEI. Clave de cache: <c>imei:{imei}</c>.
/// </summary>
public sealed class CachedDeviceOwnershipResolver : IDeviceOwnershipResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly ITelemetryQueryRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CachedDeviceOwnershipResolver> _logger;

    /// <summary>
    /// Crea un resolver con cache en memoria.
    /// </summary>
    /// <param name="repository">Repositorio de consulta para resolver ownership.</param>
    /// <param name="cache">Cache en memoria compartida.</param>
    /// <param name="logger">Logger de diagnostico.</param>
    public CachedDeviceOwnershipResolver(
        ITelemetryQueryRepository repository,
        IMemoryCache cache,
        ILogger<CachedDeviceOwnershipResolver> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetUserIdsForImeiAsync(string imei, CancellationToken cancellationToken)
    {
        string cacheKey = $"imei:{imei}";

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Guid>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<Guid> userIds = await _repository.GetActiveUserIdsByImeiAsync(imei, cancellationToken);

        _cache.Set(cacheKey, userIds, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl
        });

        _logger.LogDebug(
            "ownership_resolved imei={Imei} userCount={UserCount} cached={Cached}",
            imei,
            userIds.Count,
            false);

        return userIds;
    }
}
