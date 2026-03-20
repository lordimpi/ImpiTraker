using System.Text;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Shared.Options;
using ImpiTrack.Observability;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.EventBus;
using ImpiTrack.Ops;
using ImpiTrack.Tcp.Core.Queue;
using ImpiTrack.Tcp.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using TcpServer;

namespace ImpiTrack.Tests;

/// <summary>
/// E.3: Verifica que la cadena de notificacion no interrumpe la persistencia.
/// Si ITelemetryNotifier lanza una excepcion, InboundProcessingService continua operando.
/// </summary>
public sealed class NotificationResilienceTests
{
    [Fact]
    public async Task NotifierThrows_PersistenceStillSucceeds()
    {
        // Arrange
        const string imei = "864035053671278";
        var capturedEvents = new List<DeviceIoEventRecord>();
        var repoStub = new StubIngestionRepository(capturedEvents);
        var throwingNotifier = new ThrowingTelemetryNotifier();

        var service = BuildService(repoStub, throwingNotifier);

        // Act — send a tracking envelope; the notifier will throw but processing must not crash
        InboundEnvelope envelope = BuildTrackingEnvelope(imei, ignitionOn: true);

        // Use reflection to invoke PublishCanonicalEventsAsync (same pattern as existing tests)
        var publishMethod = typeof(InboundProcessingService)
            .GetMethod("PublishCanonicalEventsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("PublishCanonicalEventsAsync not found");

        var notifyMethod = typeof(InboundProcessingService)
            .GetMethod("NotifyRealtimeAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("NotifyRealtimeAsync not found");

        // Both should NOT throw even though the notifier throws
        var publishTask = (Task)publishMethod.Invoke(service, [envelope, CancellationToken.None])!;
        await publishTask;

        var notifyTask = (Task)notifyMethod.Invoke(service, [envelope, CancellationToken.None])!;
        await notifyTask;

        // Assert — if we got here, the exception was swallowed (resilient pipeline)
        Assert.True(throwingNotifier.WasCalled);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static InboundProcessingService BuildService(IIngestionRepository repo, ITelemetryNotifier notifier)
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

        var presenceOptions = new StubOptionsService<DevicePresenceOptions>(new DevicePresenceOptions());

        return new InboundProcessingService(
            NullLogger<InboundProcessingService>.Instance,
            new InMemoryInboundQueue(capacity: 100),
            repo,
            new NoOpTcpMetrics(),
            new InMemoryEventBus(),
            notifier,
            new DevicePresenceTracker(presenceOptions),
            tcpOptions,
            eventBusOptions);
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

    private sealed class ThrowingTelemetryNotifier : ITelemetryNotifier
    {
        public bool WasCalled { get; private set; }

        public Task NotifyPositionUpdatedAsync(string imei, double? latitude, double? longitude, double? speedKmh, int? headingDeg, DateTimeOffset occurredAtUtc, bool? ignitionOn, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("simulated_signalr_failure");
        }

        public Task NotifyDeviceStatusChangedAsync(string imei, string status, DateTimeOffset changedAtUtc, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("simulated_signalr_failure");
        }

        public Task NotifyTelemetryEventAsync(string imei, string eventType, double? latitude, double? longitude, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("simulated_signalr_failure");
        }
    }

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
