using System.Text;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Shared.Options;
using ImpiTrack.Observability;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.EventBus;
using ImpiTrack.Tcp.Core.Queue;
using Microsoft.Extensions.Logging.Abstractions;
using TcpServer;

namespace ImpiTrack.Tests;

/// <summary>
/// Tests unitarios para la deteccion de transiciones de estado ACC/PWR en InboundProcessingService.
/// B.11: Verifica que cuando IgnitionOn cambia de null/false → true, se emite un evento ACC_ON en device_events.
/// </summary>
public sealed class InboundProcessingServiceStateTests
{
    [Fact]
    public async Task InboundProcessingService_AccTransition_NullToTrue_EmitsAccOnEvent()
    {
        // Arrange
        const string imei = "864035053671278";
        var capturedEvents = new List<DeviceIoEventRecord>();
        var repoStub = new StubIngestionRepository(capturedEvents);

        var service = BuildService(repoStub);

        // First packet: IgnitionOn = null (no ACC data) — establishes IMEI in state tracker
        InboundEnvelope firstPacket = BuildTrackingEnvelope(imei, ignitionOn: null);
        await DrivePublishAsync(service, firstPacket);

        // Second packet: IgnitionOn = true → transition null → true must emit ACC_ON
        InboundEnvelope secondPacket = BuildTrackingEnvelope(imei, ignitionOn: true);
        await DrivePublishAsync(service, secondPacket);

        // Assert: debe existir exactamente un evento ACC_ON (puede haber otros como PWR_ON)
        var accEvents = capturedEvents.Where(e => e.EventCode.StartsWith("ACC")).ToList();
        Assert.Single(accEvents);
        Assert.Equal("ACC_ON", accEvents[0].EventCode);
        Assert.Equal(imei, accEvents[0].Imei);
    }

    [Fact]
    public async Task InboundProcessingService_AccTransition_FalseToTrue_EmitsAccOnEvent()
    {
        // Arrange
        const string imei = "864035053671278";
        var capturedEvents = new List<DeviceIoEventRecord>();
        var repoStub = new StubIngestionRepository(capturedEvents);
        var service = BuildService(repoStub);

        // First packet: IgnitionOn = false
        await DrivePublishAsync(service, BuildTrackingEnvelope(imei, ignitionOn: false));

        // Second packet: IgnitionOn = true → false → true must emit ACC_ON
        await DrivePublishAsync(service, BuildTrackingEnvelope(imei, ignitionOn: true));

        // Assert: debe existir exactamente un evento ACC_ON (puede haber ACC_OFF del primer packet y PWR_ON)
        var accOnEvents = capturedEvents.Where(e => e.EventCode == "ACC_ON").ToList();
        Assert.Single(accOnEvents);
        Assert.Equal(imei, accOnEvents[0].Imei);
    }

    [Fact]
    public async Task InboundProcessingService_AccTransition_TrueToFalse_EmitsAccOffEvent()
    {
        // Arrange
        const string imei = "864035053671278";
        var capturedEvents = new List<DeviceIoEventRecord>();
        var repoStub = new StubIngestionRepository(capturedEvents);
        var service = BuildService(repoStub);

        await DrivePublishAsync(service, BuildTrackingEnvelope(imei, ignitionOn: true));

        // Capture state is now true. Next packet: false → ACC_OFF
        capturedEvents.Clear();
        await DrivePublishAsync(service, BuildTrackingEnvelope(imei, ignitionOn: false));

        Assert.Single(capturedEvents);
        Assert.Equal("ACC_OFF", capturedEvents[0].EventCode);
    }

    [Fact]
    public async Task InboundProcessingService_AccNoChange_NoEventEmitted()
    {
        // Arrange
        const string imei = "864035053671278";
        var capturedEvents = new List<DeviceIoEventRecord>();
        var repoStub = new StubIngestionRepository(capturedEvents);
        var service = BuildService(repoStub);

        // Send same IgnitionOn=true twice — second should not emit any event
        await DrivePublishAsync(service, BuildTrackingEnvelope(imei, ignitionOn: true));
        capturedEvents.Clear();

        await DrivePublishAsync(service, BuildTrackingEnvelope(imei, ignitionOn: true));

        Assert.Empty(capturedEvents);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static InboundProcessingService BuildService(IIngestionRepository repo)
    {
        var tcpOptions = new StubOptionsService<TcpServerOptions>(new TcpServerOptions
        {
            Pipeline = new TcpPipelineOptions { ConsumerWorkers = 1 }
        });

        var eventBusOptions = new StubOptionsService<EventBusOptions>(new EventBusOptions
        {
            Provider = "InMemory",
            MaxPublishRetries = 0,
            EnableDlq = false
        });

        return new InboundProcessingService(
            NullLogger<InboundProcessingService>.Instance,
            new InMemoryInboundQueue(capacity: 100),
            repo,
            new NoOpTcpMetrics(),
            new InMemoryEventBus(),
            tcpOptions,
            eventBusOptions);
    }

    /// <summary>
    /// Invoca el flujo de procesamiento interno equivalente al que RunConsumerAsync ejecuta
    /// procesando un solo envelope: persiste + publica eventos canonicos.
    /// Se usa reflexion para acceder al metodo privado PublishCanonicalEventsAsync
    /// ya que DetectAndPersistStateChangesAsync es invocado desde ahi.
    /// </summary>
    private static async Task DrivePublishAsync(InboundProcessingService service, InboundEnvelope envelope)
    {
        var method = typeof(InboundProcessingService)
            .GetMethod("PublishCanonicalEventsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("PublishCanonicalEventsAsync not found");

        var task = (Task)method.Invoke(service, [envelope, CancellationToken.None])!;
        await task;
    }

    private static InboundEnvelope BuildTrackingEnvelope(string imei, bool? ignitionOn)
    {
        string payload = $"imei:{imei},tracker,260314230546,100%,F,040546.000,A,0227.34852,N,07635.46362,W,12.29,155.12,,{(ignitionOn.HasValue ? (ignitionOn.Value ? "1" : "0") : "")},0,0.00%;";

        var message = new ParsedMessage(
            ProtocolId.Coban,
            MessageType.Tracking,
            imei,
            Encoding.ASCII.GetBytes(payload),
            payload,
            DateTimeOffset.UtcNow,
            GpsTimeUtc: DateTimeOffset.UtcNow,
            Latitude: -2.455,
            Longitude: -76.591,
            SpeedKmh: 12.29,
            HeadingDeg: 155,
            IsTelemetryUsable: true,
            TelemetryError: null,
            IgnitionOn: ignitionOn,
            PowerConnected: true);

        return new InboundEnvelope(
            SessionId.New(),
            PacketId.New(),
            5001,
            "127.0.0.1",
            message,
            DateTimeOffset.UtcNow);
    }

    // ─── Stubs ───────────────────────────────────────────────────────────────

    private sealed class StubIngestionRepository : IIngestionRepository
    {
        private readonly List<DeviceIoEventRecord> _captured;

        public StubIngestionRepository(List<DeviceIoEventRecord> captured) => _captured = captured;

        public Task AddRawPacketAsync(RawPacketRecord record, long backlog, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpsertSessionAsync(SessionRecord session, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<PersistEnvelopeResult> PersistEnvelopeAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
            => Task.FromResult(new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted));

        public Task InsertDeviceIoEventAsync(DeviceIoEventRecord record, CancellationToken cancellationToken)
        {
            _captured.Add(record);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpTcpMetrics : ITcpMetrics
    {
        public void RecordConnectionOpened(int port) { }
        public void RecordConnectionClosed(int port) { }
        public void RecordFrameReceived(int port, ProtocolId protocol) { }
        public void RecordFrameInvalid(int port, ProtocolId protocol) { }
        public void RecordParseResult(int port, ProtocolId protocol, bool success) { }
        public void RecordAck(int port, ProtocolId protocol, double latencyMs) { }
        public void RecordQueueBacklog(int port, ProtocolId protocol, long backlog) { }
        public void RecordPersistLatency(int port, ProtocolId protocol, double latencyMs) { }
        public void RecordRawQueueBacklog(int port, ProtocolId protocol, long backlog) { }
        public void RecordRawQueueDrop(int port, ProtocolId protocol) { }
        public void RecordDedupeDrop(int port, ProtocolId protocol) { }
        public void RecordEventPublishFailure(int port, ProtocolId protocol, string eventType) { }
        public void RecordEventPublishSuccess(int port, ProtocolId protocol, string eventType, double latencyMs) { }
        public void RecordEventPublishRetry(int port, ProtocolId protocol, string eventType) { }
        public void RecordEventDlq(int port, ProtocolId protocol, string eventType) { }
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
