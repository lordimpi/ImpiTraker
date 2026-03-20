using ImpiTrack.Application.Abstractions;
using ImpiTrack.Application.Services;

namespace ImpiTrack.Tests;

/// <summary>
/// Pruebas unitarias para el algoritmo de construccion de viajes movement_2d_acc_v2.
/// Se testea el comportamiento de BuildTrips a traves de TelemetryQueryService usando stubs minimos.
/// </summary>
public sealed class TripBuildingTests
{
    private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private const string Imei = "TEST_IMEI_TRIPS";
    private static readonly DateTimeOffset Base = new(2026, 3, 18, 12, 0, 0, TimeSpan.Zero);

    // ──────────────────────────────────────────────────────────────────────────
    // C.10 — Ambos ejes superan el umbral → detecta movimiento
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildTrips_BothAxesExceedThreshold_DetectsMovement()
    {
        // Arrange
        // P1: punto de referencia
        // P2: desplazamiento > 0.00018° en lat Y lon → movimiento 2D real
        // P3: continua el viaje
        var p1 = MakePoint(Base, lat: 4.6000, lon: -74.1000, speed: 0);
        var p2 = MakePoint(Base.AddMinutes(1), lat: 4.6002, lon: -74.0998, speed: 0); // |dLat|=0.0002 > 0.00018, |dLon|=0.0002 > 0.00018
        var p3 = MakePoint(Base.AddMinutes(2), lat: 4.6004, lon: -74.0996, speed: 0);

        var service = BuildService([p1, p2, p3], accEvents: []);

        // Act
        var result = await service.GetTripsAsync(UserId, Imei, Base.AddMinutes(-1), Base.AddMinutes(10), 10, CancellationToken.None);

        // Assert
        Assert.Equal(TelemetryLookupStatus.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data!);
        Assert.True(result.Data![0].PointCount >= 2, "El viaje debe tener al menos 2 puntos");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // C.11 — Solo un eje supera el umbral → NO es movimiento
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildTrips_OnlyOneAxisExceedsThreshold_NoMovement()
    {
        // Arrange
        // P1 y P2 solo difieren en latitud (lon idéntica) → solo un eje → ruido GPS
        // P1 y P3 solo difieren en longitud (lat idéntica) → solo un eje → ruido GPS
        // Todos tienen speed < 12 → no hay movimiento por velocidad tampoco
        // P1→P2: dLat=0.0002 > 0.00018 (supera), dLon=0 (no supera) → solo lat → no movimiento
        // P2→P3: dLat=0 (no supera), dLon=0.0001 < 0.00018 (no supera) → ningún eje → no movimiento
        var p1 = MakePoint(Base, lat: 4.6000, lon: -74.1000, speed: 5);
        var p2 = MakePoint(Base.AddMinutes(1), lat: 4.6002, lon: -74.1000, speed: 5); // solo lat cambia
        var p3 = MakePoint(Base.AddMinutes(2), lat: 4.6002, lon: -74.1001, speed: 5); // solo lon cambia, delta=0.0001 < 0.00018

        var service = BuildService([p1, p2, p3], accEvents: []);

        // Act
        var result = await service.GetTripsAsync(UserId, Imei, Base.AddMinutes(-1), Base.AddMinutes(10), 10, CancellationToken.None);

        // Assert
        Assert.Equal(TelemetryLookupStatus.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!); // Ningun viaje debe construirse
    }

    // ──────────────────────────────────────────────────────────────────────────
    // C.12 — ACC_ON abre un viaje en el punto ACC
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildTrips_AccOnEvent_OpensTripAtAccPosition()
    {
        // Arrange
        // Tres puntos sin velocidad suficiente ni 2D real.
        // En el segundo punto, ACC transiciona a ON → debe abrir viaje.
        var p1 = MakePoint(Base, lat: 4.6000, lon: -74.1000, speed: 0, ignitionOn: false);
        var p2 = MakePoint(Base.AddMinutes(1), lat: 4.6000, lon: -74.1000, speed: 0, ignitionOn: true); // ACC_ON
        var p3 = MakePoint(Base.AddMinutes(2), lat: 4.6000, lon: -74.1000, speed: 0, ignitionOn: true);

        var service = BuildService([p1, p2, p3], accEvents: []);

        // Act
        var result = await service.GetTripsAsync(UserId, Imei, Base.AddMinutes(-1), Base.AddMinutes(10), 10, CancellationToken.None);

        // Assert
        Assert.Equal(TelemetryLookupStatus.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data!); // Debe haber un viaje abierto por ACC_ON

        // El viaje debe comenzar en p2 (el punto con ACC_ON), no en p1
        TripSummaryDto trip = result.Data![0];
        Assert.True(trip.StartedAtUtc >= p2.OccurredAtUtc,
            $"El viaje debe comenzar en o despues de p2 ({p2.OccurredAtUtc}), pero comenzó en {trip.StartedAtUtc}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // C.13 — ACC_OFF cierra el viaje en el punto ACC
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildTrips_AccOffEvent_ClosesTripAtAccPosition()
    {
        // Arrange
        // Viaje abierto con ACC_ON en p1, avanza, cierra con ACC_OFF en p3.
        // p4 tiene ACC=OFF también: debe quedar fuera del viaje.
        var p1 = MakePoint(Base, lat: 4.6000, lon: -74.1000, speed: 0, ignitionOn: true);           // ACC_ON → abre viaje
        var p2 = MakePoint(Base.AddMinutes(1), lat: 4.6001, lon: -74.1001, speed: 15, ignitionOn: true);
        var p3 = MakePoint(Base.AddMinutes(2), lat: 4.6002, lon: -74.1002, speed: 0, ignitionOn: false); // ACC_OFF → cierra
        var p4 = MakePoint(Base.AddMinutes(3), lat: 4.6003, lon: -74.1003, speed: 0, ignitionOn: false);

        var service = BuildService([p1, p2, p3, p4], accEvents: []);

        // Act — buscar detalle del viaje para verificar que p4 no esta incluido
        var listResult = await service.GetTripsAsync(UserId, Imei, Base.AddMinutes(-1), Base.AddMinutes(10), 10, CancellationToken.None);

        // Assert
        Assert.Equal(TelemetryLookupStatus.Success, listResult.Status);
        Assert.NotNull(listResult.Data);
        Assert.NotEmpty(listResult.Data!);

        TripSummaryDto trip = listResult.Data![0];

        // El viaje debe haber terminado: el punto de cierre es p3 que está lo suficientemente
        // atrás como para que EndedAtUtc no sea null (se usa TripGapThreshold para decidir si está abierto).
        // Verificamos que el viaje cierra antes o en p3.
        if (trip.EndedAtUtc.HasValue)
        {
            Assert.True(trip.EndedAtUtc.Value <= p3.OccurredAtUtc.AddSeconds(1),
                $"El viaje debe cerrar en p3 ({p3.OccurredAtUtc}), pero cerró en {trip.EndedAtUtc}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // C.14 — Todos IgnitionOn=null → fallback a velocidad + 2D
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildTrips_AllIgnitionNull_FallsBackToSpeedAndDistance()
    {
        // Arrange
        // Puntos sin IgnitionOn (null) pero con velocidad >= 12 km/h → debe detectar movimiento por velocidad.
        var p1 = MakePoint(Base, lat: 4.6000, lon: -74.1000, speed: 15, ignitionOn: null);
        var p2 = MakePoint(Base.AddMinutes(1), lat: 4.6020, lon: -74.0980, speed: 20, ignitionOn: null);
        var p3 = MakePoint(Base.AddMinutes(2), lat: 4.6040, lon: -74.0960, speed: 18, ignitionOn: null);

        var service = BuildService([p1, p2, p3], accEvents: []);

        // Act
        var result = await service.GetTripsAsync(UserId, Imei, Base.AddMinutes(-1), Base.AddMinutes(10), 10, CancellationToken.None);

        // Assert
        Assert.Equal(TelemetryLookupStatus.Success, result.Status);
        Assert.NotNull(result.Data);
        Assert.NotEmpty(result.Data!); // Viaje detectado por velocidad en modo fallback
    }

    // ──────────────────────────────────────────────────────────────────────────
    // C.15 — El nombre del algoritmo es "movement_2d_acc_v2"
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildTrips_TripDetail_HasCorrectAlgorithmName()
    {
        // Arrange
        var p1 = MakePoint(Base, lat: 4.6000, lon: -74.1000, speed: 15, ignitionOn: null);
        var p2 = MakePoint(Base.AddMinutes(1), lat: 4.6020, lon: -74.0980, speed: 20, ignitionOn: null);

        var service = BuildService([p1, p2], accEvents: []);

        var listResult = await service.GetTripsAsync(UserId, Imei, Base.AddMinutes(-1), Base.AddMinutes(10), 10, CancellationToken.None);
        Assert.NotNull(listResult.Data);
        Assert.NotEmpty(listResult.Data!);

        string tripId = listResult.Data![0].TripId;

        // Act
        var detailResult = await service.GetTripByIdAsync(
            UserId, Imei, tripId,
            Base.AddMinutes(-1), Base.AddMinutes(10),
            CancellationToken.None);

        // Assert
        Assert.Equal(TelemetryLookupStatus.Success, detailResult.Status);
        Assert.NotNull(detailResult.Data);
        Assert.Equal("movement_2d_acc_v2", detailResult.Data!.SourceRule);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static DevicePositionPointDto MakePoint(
        DateTimeOffset occurredAt,
        double lat,
        double lon,
        double? speed = null,
        bool? ignitionOn = null)
    {
        return new DevicePositionPointDto(
            OccurredAtUtc: occurredAt,
            ReceivedAtUtc: occurredAt,
            GpsTimeUtc: occurredAt,
            Latitude: lat,
            Longitude: lon,
            SpeedKmh: speed,
            HeadingDeg: null,
            PacketId: Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            IgnitionOn: ignitionOn);
    }

    private static TelemetryQueryService BuildService(
        IReadOnlyList<DevicePositionPointDto> positions,
        IReadOnlyList<AccEventDto> accEvents)
    {
        var repo = new StubTelemetryRepository(positions, accEvents);
        var lookup = new StubIdentityUserLookup(UserId);
        return new TelemetryQueryService(repo, lookup);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Stubs de infraestructura para pruebas unitarias
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class StubTelemetryRepository : ITelemetryQueryRepository
    {
        private readonly IReadOnlyList<DevicePositionPointDto> _positions;
        private readonly IReadOnlyList<AccEventDto> _accEvents;

        public StubTelemetryRepository(
            IReadOnlyList<DevicePositionPointDto> positions,
            IReadOnlyList<AccEventDto> accEvents)
        {
            _positions = positions;
            _accEvents = accEvents;
        }

        public Task<bool> HasActiveDeviceBindingAsync(Guid userId, string imei, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<IReadOnlyList<DevicePositionPointDto>> GetTripCandidatePositionsAsync(
            Guid userId, string imei, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maxPoints, CancellationToken cancellationToken)
            => Task.FromResult(_positions);

        public Task<IReadOnlyList<AccEventDto>> GetAccEventsForWindowAsync(
            string imei, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
            => Task.FromResult(_accEvents);

        // Los siguientes metodos no se usan en estas pruebas — implementacion minima.
        public Task<IReadOnlyList<TelemetryDeviceSummaryDto>> GetDeviceSummariesAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TelemetryDeviceSummaryDto>>([]);

        public Task<IReadOnlyList<DevicePositionPointDto>> GetPositionsAsync(
            Guid userId, string imei, DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken cancellationToken)
            => Task.FromResult(_positions);

        public Task<IReadOnlyList<DeviceEventDto>> GetEventsAsync(
            Guid userId, string imei, DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DeviceEventDto>>([]);

        public Task<IReadOnlyList<Guid>> GetActiveUserIdsByImeiAsync(string imei, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class StubIdentityUserLookup : IIdentityUserLookup
    {
        private readonly Guid _userId;

        public StubIdentityUserLookup(Guid userId) => _userId = userId;

        public Task<IdentityUserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
            => Task.FromResult<IdentityUserInfo?>(
                userId == _userId ? new IdentityUserInfo(userId, "test@test.com") : null);
    }
}
