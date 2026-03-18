using ImpiTrack.Application.Abstractions;
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

    /// <summary>Umbral de velocidad para detectar movimiento. Elegido en 12 km/h para filtrar deriva GPS en vehiculos detenidos.</summary>
    private const double MovingSpeedThresholdKmh = 12d;

    /// <summary>Umbral por eje en grados decimales para deteccion 2D de movimiento. 0.00018° ≈ 20m a latitudes ecuatoriales.</summary>
    private const double MovingAreaThresholdDeg = 0.00018d;

    private const string TripSourceRule = "movement_2d_acc_v2";

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

        // Consultar posiciones y eventos ACC en paralelo para minimizar latencia.
        Task<IReadOnlyList<DevicePositionPointDto>> positionsTask = _telemetryQueryRepository.GetTripCandidatePositionsAsync(
            userId,
            normalizedImei,
            fromValue,
            toValue,
            MaxTripCandidatePoints,
            cancellationToken);

        Task<IReadOnlyList<AccEventDto>> accEventsTask = _telemetryQueryRepository.GetAccEventsForWindowAsync(
            normalizedImei,
            fromValue,
            toValue,
            cancellationToken);

        await Task.WhenAll(positionsTask, accEventsTask);

        IReadOnlyList<DevicePositionPointDto> positions = positionsTask.Result;
        IReadOnlyList<AccEventDto> accEvents = accEventsTask.Result;

        // Anotar posiciones con el estado IgnitionOn derivado del evento ACC mas reciente anterior a cada punto.
        IReadOnlyList<DevicePositionPointDto> annotated = accEvents.Count == 0
            ? positions
            : AnnotateWithAccState(positions, accEvents);

        return new TelemetryLookupResult<IReadOnlyList<DevicePositionPointDto>>(TelemetryLookupStatus.Success, annotated);
    }

    /// <summary>
    /// Anota cada posicion con el ultimo estado ACC conocido previo a su timestamp.
    /// Si no hay ningun evento ACC anterior a una posicion, IgnitionOn queda como null.
    /// </summary>
    private static IReadOnlyList<DevicePositionPointDto> AnnotateWithAccState(
        IReadOnlyList<DevicePositionPointDto> positions,
        IReadOnlyList<AccEventDto> accEvents)
    {
        // accEvents ya viene ordenado ASC por OccurredAtUtc desde el repositorio.
        DevicePositionPointDto[] result = new DevicePositionPointDto[positions.Count];
        int i = 0;
        int accIndex = 0;
        bool? currentAccState = null;

        foreach (DevicePositionPointDto point in positions.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.ReceivedAtUtc))
        {
            // Avanzar el puntero de eventos ACC hasta el ultimo evento anterior o igual al timestamp del punto.
            while (accIndex < accEvents.Count && accEvents[accIndex].OccurredAtUtc <= point.OccurredAtUtc)
            {
                currentAccState = accEvents[accIndex].IsOn;
                accIndex++;
            }

            result[i++] = point with { IgnitionOn = currentAccState };
        }

        return result;
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

        // Determinar si hay datos ACC disponibles en la secuencia.
        // Si todos los puntos tienen IgnitionOn=null → modo fallback puro (velocidad + 2D).
        bool hasAccData = candidates.Any(x => x.IgnitionOn.HasValue);

        List<BuiltTrip> trips = [];
        List<DevicePositionPointDto>? current = null;
        DevicePositionPointDto? previous = null;
        DevicePositionPointDto? previousMoving = null;
        bool? previousIgnitionState = null;

        foreach (DevicePositionPointDto point in candidates.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.ReceivedAtUtc))
        {
            bool isMoving = IsMovingPoint(previous, point);

            // Detectar transicion ACC: true→true no es transicion, false→true O null→true = ACC_ON event.
            bool isAccOn = hasAccData
                && point.IgnitionOn == true
                && previousIgnitionState != true;

            // Detectar ACC_OFF: transicion de true a false (o null si el ultimo conocido era true).
            bool isAccOff = hasAccData
                && point.IgnitionOn == false
                && previousIgnitionState == true;

            if (current is null)
            {
                // Abrir viaje: ACC_ON es señal primaria, movimiento 2D/velocidad es secundario.
                bool shouldOpen = isAccOn || (!hasAccData && isMoving);
                if (shouldOpen)
                {
                    current = [];
                    if (previous is not null && !isAccOn)
                    {
                        // Solo incluir punto anterior como cabeza del viaje en modo speed/2D.
                        current.Add(previous);
                    }

                    current.Add(point);
                }

                previous = point;
                previousIgnitionState = point.IgnitionOn ?? previousIgnitionState;
                if (isMoving)
                {
                    previousMoving = point;
                }

                continue;
            }

            // Cerrar viaje por ACC_OFF: señal primaria de fin.
            if (isAccOff)
            {
                current.Add(point);
                AddTripIfValid(trips, imei, current, nowUtc);
                current = null;
                previous = point;
                previousIgnitionState = false;
                previousMoving = null;
                continue;
            }

            // Cerrar viaje por gap temporal (logica existente de brecha).
            DevicePositionPointDto reference = previousMoving ?? current[^1];
            if (point.OccurredAtUtc - reference.OccurredAtUtc > TripGapThreshold)
            {
                AddTripIfValid(trips, imei, current, nowUtc);

                // Abrir nuevo viaje inmediatamente si hay movimiento o ACC_ON en el punto actual.
                bool shouldOpenNext = isAccOn || (!hasAccData && isMoving);
                current = shouldOpenNext
                    ? previous is not null ? [previous, point] : [point]
                    : null;
                previous = point;
                previousIgnitionState = point.IgnitionOn ?? previousIgnitionState;
                previousMoving = isMoving ? point : null;
                continue;
            }

            current.Add(point);
            previous = point;
            previousIgnitionState = point.IgnitionOn ?? previousIgnitionState;
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

    /// <summary>
    /// Determina si un punto GPS representa movimiento real respecto al punto anterior.
    /// Criterio primario: velocidad >= 12 km/h.
    /// Criterio secundario: ambos ejes lat Y lon superan el umbral de 0.00018° (≈20m) — requiere movimiento en AMBAS dimensiones
    /// para filtrar deriva GPS de un solo eje.
    /// </summary>
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

        // Deteccion 2D: ambos ejes deben superar el umbral. Un solo eje = ruido GPS, no movimiento real.
        return Math.Abs(current.Latitude - previous.Latitude) >= MovingAreaThresholdDeg
            && Math.Abs(current.Longitude - previous.Longitude) >= MovingAreaThresholdDeg;
    }

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
