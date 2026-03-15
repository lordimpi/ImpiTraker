using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using System.Security.Cryptography;
using System.Text;

namespace ImpiTrack.Application.Services;

/// <summary>
/// Implementacion de casos de uso para lectura funcional de telemetria.
/// </summary>
public sealed class TelemetryQueryService : ITelemetryQueryService
{
    private const int DefaultWindowHours = 24;
    private const int DefaultPositionsLimit = 500;
    private const int DefaultEventsLimit = 100;
    private const int DefaultTripsLimit = 50;
    private const int MaxPositionsLimit = 500;
    private const int MaxEventsLimit = 100;
    private const int MaxTripsLimit = 200;
    private const int MaxTripCandidatePoints = 5000;
    private static readonly TimeSpan TripGapThreshold = TimeSpan.FromMinutes(10);
    private const double MovingSpeedThresholdKmh = 5d;
    private const double MovingDistanceThresholdMeters = 100d;
    private const string TripSourceRule = "movement_gap_v1";

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

    /// <inheritdoc />
    public async Task<TelemetryLookupResult<IReadOnlyList<TripSummaryDto>>> GetTripsAsync(
        Guid userId,
        string imei,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>> candidatesResult = await GetTripCandidatesAsync(
            userId,
            imei,
            fromUtc,
            toUtc,
            cancellationToken);

        if (candidatesResult.Status != TelemetryLookupStatus.Success)
        {
            return new TelemetryLookupResult<IReadOnlyList<TripSummaryDto>>(candidatesResult.Status, null);
        }

        int normalizedLimit = Math.Clamp(limit ?? DefaultTripsLimit, 1, MaxTripsLimit);
        IReadOnlyList<TripSummaryDto> trips = BuildTrips(
            imei.Trim(),
            candidatesResult.Data ?? [],
            DateTimeOffset.UtcNow)
            .Take(normalizedLimit)
            .Select(ToSummary)
            .ToArray();

        return new TelemetryLookupResult<IReadOnlyList<TripSummaryDto>>(TelemetryLookupStatus.Success, trips);
    }

    /// <inheritdoc />
    public async Task<TelemetryLookupResult<TripDetailDto>> GetTripByIdAsync(
        Guid userId,
        string imei,
        string tripId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>> candidatesResult = await GetTripCandidatesAsync(
            userId,
            imei,
            fromUtc,
            toUtc,
            cancellationToken);

        if (candidatesResult.Status != TelemetryLookupStatus.Success)
        {
            return new TelemetryLookupResult<TripDetailDto>(candidatesResult.Status, null);
        }

        string normalizedTripId = tripId.Trim();
        BuiltTrip? trip = BuildTrips(
            imei.Trim(),
            candidatesResult.Data ?? [],
            DateTimeOffset.UtcNow)
            .FirstOrDefault(x => string.Equals(x.TripId, normalizedTripId, StringComparison.Ordinal));

        if (trip is null)
        {
            return new TelemetryLookupResult<TripDetailDto>(TelemetryLookupStatus.TripNotFound, null);
        }

        return new TelemetryLookupResult<TripDetailDto>(TelemetryLookupStatus.Success, ToDetail(trip));
    }

    private async Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _identityUserLookup.FindByIdAsync(userId, cancellationToken) is not null;
    }

    private async Task<TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>> GetTripCandidatesAsync(
        Guid userId,
        string imei,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
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

        (DateTimeOffset fromValue, DateTimeOffset toValue, _) = NormalizeWindow(
            fromUtc,
            toUtc,
            limit: null,
            DefaultTripsLimit,
            MaxTripsLimit);

        IReadOnlyList<DevicePositionPointDto> items = await _telemetryQueryRepository.GetTripCandidatePositionsAsync(
            userId,
            normalizedImei,
            fromValue,
            toValue,
            MaxTripCandidatePoints,
            cancellationToken);

        return new TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>(TelemetryLookupStatus.Success, items);
    }

    private static IReadOnlyList<BuiltTrip> BuildTrips(
        string imei,
        IReadOnlyList<DevicePositionPointDto> candidates,
        DateTimeOffset nowUtc)
    {
        if (candidates.Count == 0)
        {
            return [];
        }

        List<BuiltTrip> trips = [];
        List<DevicePositionPointDto>? current = null;
        DevicePositionPointDto? previous = null;
        DevicePositionPointDto? previousMoving = null;

        foreach (DevicePositionPointDto point in candidates.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.ReceivedAtUtc))
        {
            bool isMoving = IsMovingPoint(previous, point);

            if (current is null)
            {
                if (isMoving)
                {
                    current = [];
                    if (previous is not null)
                    {
                        current.Add(previous);
                    }

                    current.Add(point);
                }

                previous = point;
                if (isMoving)
                {
                    previousMoving = point;
                }

                continue;
            }

            DevicePositionPointDto reference = previousMoving ?? current[^1];
            if (point.OccurredAtUtc - reference.OccurredAtUtc > TripGapThreshold)
            {
                AddTripIfValid(trips, imei, current, nowUtc);
                current = isMoving
                    ? previous is not null ? [previous, point] : [point]
                    : null;
                previous = point;
                previousMoving = isMoving ? point : null;
                continue;
            }

            current.Add(point);
            previous = point;
            if (isMoving)
            {
                previousMoving = point;
            }
        }

        AddTripIfValid(trips, imei, current, nowUtc);

        return trips
            .OrderByDescending(x => x.StartedAtUtc)
            .ToArray();
    }

    private static void AddTripIfValid(List<BuiltTrip> trips, string imei, List<DevicePositionPointDto>? points, DateTimeOffset nowUtc)
    {
        if (points is null || points.Count < 2)
        {
            return;
        }

        DevicePositionPointDto start = points[0];
        DevicePositionPointDto end = points[^1];
        double? maxSpeed = points.Where(x => x.SpeedKmh.HasValue).Select(x => x.SpeedKmh!.Value).DefaultIfEmpty().Max();
        if (maxSpeed == 0d && points.All(x => !x.SpeedKmh.HasValue))
        {
            maxSpeed = null;
        }

        double? avgSpeed = null;
        double[] speeds = points.Where(x => x.SpeedKmh.HasValue).Select(x => x.SpeedKmh!.Value).ToArray();
        if (speeds.Length > 0)
        {
            avgSpeed = speeds.Average();
        }

        string tripId = CreateTripId(imei, start.OccurredAtUtc, start.PacketId);
        DateTimeOffset? endedAtUtc = nowUtc - end.OccurredAtUtc <= TripGapThreshold
            ? null
            : end.OccurredAtUtc;

        trips.Add(new BuiltTrip(
            tripId,
            imei,
            start.OccurredAtUtc,
            endedAtUtc,
            points.ToArray(),
            maxSpeed,
            avgSpeed));
    }

    private static bool IsMovingPoint(DevicePositionPointDto? previous, DevicePositionPointDto current)
    {
        if (current.SpeedKmh.GetValueOrDefault() >= MovingSpeedThresholdKmh)
        {
            return true;
        }

        if (previous is null)
        {
            return false;
        }

        return CalculateDistanceMeters(previous.Latitude, previous.Longitude, current.Latitude, current.Longitude)
            >= MovingDistanceThresholdMeters;
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusMeters = 6371000d;
        double dLat = DegreesToRadians(lat2 - lat1);
        double dLon = DegreesToRadians(lon2 - lon1);
        double startLat = DegreesToRadians(lat1);
        double endLat = DegreesToRadians(lat2);

        double a =
            Math.Pow(Math.Sin(dLat / 2d), 2d) +
            Math.Cos(startLat) * Math.Cos(endLat) * Math.Pow(Math.Sin(dLon / 2d), 2d);

        double c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);

    private static string CreateTripId(string imei, DateTimeOffset startedAtUtc, Guid firstPacketId)
    {
        string raw = $"{imei}|{startedAtUtc:O}|{firstPacketId:D}";
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes[..16]).ToLowerInvariant();
    }

    private static TripSummaryDto ToSummary(BuiltTrip trip)
    {
        return new TripSummaryDto(
            trip.TripId,
            trip.Imei,
            trip.StartedAtUtc,
            trip.EndedAtUtc,
            trip.Points.Count,
            trip.MaxSpeedKmh,
            trip.AvgSpeedKmh,
            trip.Points[0],
            trip.Points[^1]);
    }

    private static TripDetailDto ToDetail(BuiltTrip trip)
    {
        return new TripDetailDto(
            trip.TripId,
            trip.Imei,
            trip.StartedAtUtc,
            trip.EndedAtUtc,
            trip.Points.Count,
            trip.MaxSpeedKmh,
            trip.AvgSpeedKmh,
            trip.Points,
            trip.Points[0],
            trip.Points[^1],
            TripSourceRule);
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

    private sealed record BuiltTrip(
        string TripId,
        string Imei,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        IReadOnlyList<DevicePositionPointDto> Points,
        double? MaxSpeedKmh,
        double? AvgSpeedKmh);
}
