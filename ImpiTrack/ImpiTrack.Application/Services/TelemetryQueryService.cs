using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;

namespace ImpiTrack.Application.Services;

/// <summary>
/// Implementacion de casos de uso para lectura funcional de telemetria.
/// </summary>
public sealed class TelemetryQueryService : ITelemetryQueryService
{
    private const int DefaultWindowHours = 24;
    private const int DefaultPositionsLimit = 500;
    private const int DefaultEventsLimit = 100;
    private const int MaxPositionsLimit = 500;
    private const int MaxEventsLimit = 100;

    private readonly ITelemetryQueryRepository _telemetryQueryRepository;
    private readonly IIdentityUserLookup _identityUserLookup;

    /// <summary>
    /// Crea una instancia del servicio de consulta de telemetria.
    /// </summary>
    /// <param name="telemetryQueryRepository">Repositorio de lectura de telemetria.</param>
    /// <param name="identityUserLookup">Consulta de usuarios de identidad.</param>
    public TelemetryQueryService(
        ITelemetryQueryRepository telemetryQueryRepository,
        IIdentityUserLookup identityUserLookup)
    {
        _telemetryQueryRepository = telemetryQueryRepository;
        _identityUserLookup = identityUserLookup;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TelemetryDeviceSummaryDto>?> GetDeviceSummariesAsync(Guid userId, CancellationToken cancellationToken)
    {
        IdentityUserInfo? user = await _identityUserLookup.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return await _telemetryQueryRepository.GetDeviceSummariesAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>> GetPositionsAsync(
        Guid userId,
        string imei,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return new TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>(TelemetryLookupStatus.UserNotFound, null);
        }

        string normalizedImei = imei.Trim();
        if (!await _telemetryQueryRepository.HasActiveDeviceBindingAsync(userId, normalizedImei, cancellationToken))
        {
            return new TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>(TelemetryLookupStatus.DeviceBindingNotFound, null);
        }

        (DateTimeOffset fromValue, DateTimeOffset toValue, int normalizedLimit) = NormalizeWindow(
            fromUtc,
            toUtc,
            limit,
            DefaultPositionsLimit,
            MaxPositionsLimit);

        IReadOnlyList<DevicePositionPointDto> items = await _telemetryQueryRepository.GetPositionsAsync(
            userId,
            normalizedImei,
            fromValue,
            toValue,
            normalizedLimit,
            cancellationToken);

        return new TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>(TelemetryLookupStatus.Success, items);
    }

    /// <inheritdoc />
    public async Task<TelemetryLookupResult<IReadOnlyList<DeviceEventDto>>> GetEventsAsync(
        Guid userId,
        string imei,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        if (!await UserExistsAsync(userId, cancellationToken))
        {
            return new TelemetryLookupResult<IReadOnlyList<DeviceEventDto>>(TelemetryLookupStatus.UserNotFound, null);
        }

        string normalizedImei = imei.Trim();
        if (!await _telemetryQueryRepository.HasActiveDeviceBindingAsync(userId, normalizedImei, cancellationToken))
        {
            return new TelemetryLookupResult<IReadOnlyList<DeviceEventDto>>(TelemetryLookupStatus.DeviceBindingNotFound, null);
        }

        (DateTimeOffset fromValue, DateTimeOffset toValue, int normalizedLimit) = NormalizeWindow(
            fromUtc,
            toUtc,
            limit,
            DefaultEventsLimit,
            MaxEventsLimit);

        IReadOnlyList<DeviceEventDto> items = await _telemetryQueryRepository.GetEventsAsync(
            userId,
            normalizedImei,
            fromValue,
            toValue,
            normalizedLimit,
            cancellationToken);

        return new TelemetryLookupResult<IReadOnlyList<DeviceEventDto>>(TelemetryLookupStatus.Success, items);
    }

    private async Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _identityUserLookup.FindByIdAsync(userId, cancellationToken) is not null;
    }

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc, int Limit) NormalizeWindow(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        int defaultLimit,
        int maxLimit)
    {
        DateTimeOffset toValue = toUtc ?? DateTimeOffset.UtcNow;
        DateTimeOffset fromValue = fromUtc ?? toValue.AddHours(-DefaultWindowHours);
        int normalizedLimit = Math.Clamp(limit ?? defaultLimit, 1, maxLimit);
        return (fromValue, toValue, normalizedLimit);
    }
}
