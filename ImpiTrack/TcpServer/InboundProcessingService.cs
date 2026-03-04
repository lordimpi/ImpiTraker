using System.Diagnostics;
using System.Collections.Concurrent;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.IOptionPattern;
using ImpiTrack.Observability;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.EventBus;
using ImpiTrack.Tcp.Core.Queue;

namespace TcpServer;

/// <summary>
/// Servicio consumidor en segundo plano que drena envelopes de la cola entrante.
/// </summary>
public sealed class InboundProcessingService : BackgroundService
{
    private readonly ILogger<InboundProcessingService> _logger;
    private readonly IInboundQueue _inboundQueue;
    private readonly IIngestionRepository _ingestionRepository;
    private readonly ITcpMetrics _tcpMetrics;
    private readonly IEventBus _eventBus;
    private readonly EventBusOptions _eventBusOptions;
    private readonly int _workerCount;
    private readonly ConcurrentDictionary<string, byte> _simulatedFailureOnceTracker = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Crea un servicio consumidor de cola usando la concurrencia de workers configurada.
    /// </summary>
    /// <param name="logger">Instancia de logger.</param>
    /// <param name="inboundQueue">Abstraccion de cola entrante.</param>
    /// <param name="ingestionRepository">Repositorio de persistencia downstream.</param>
    /// <param name="tcpMetrics">Publicador de metricas operativas TCP.</param>
    /// <param name="eventBus">Bus de eventos interno para contratos canonicos.</param>
    /// <param name="optionsService">Opciones del servidor.</param>
    /// <param name="eventBusOptionsService">Opciones del bus de eventos.</param>
    public InboundProcessingService(
        ILogger<InboundProcessingService> logger,
        IInboundQueue inboundQueue,
        IIngestionRepository ingestionRepository,
        ITcpMetrics tcpMetrics,
        IEventBus eventBus,
        IGenericOptionsService<TcpServerOptions> optionsService,
        IGenericOptionsService<EventBusOptions> eventBusOptionsService)
    {
        _logger = logger;
        _inboundQueue = inboundQueue;
        _ingestionRepository = ingestionRepository;
        _tcpMetrics = tcpMetrics;
        _eventBus = eventBus;
        TcpServerOptions tcpOptions = optionsService.GetOptions();
        _workerCount = Math.Max(1, tcpOptions.Pipeline.ConsumerWorkers);

        _eventBusOptions = eventBusOptionsService.GetOptions();
    }

    /// <summary>
    /// Arranca workers consumidores y los mantiene activos hasta el apagado del host.
    /// </summary>
    /// <param name="stoppingToken">Token de cancelacion de apagado.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task[] workers = new Task[_workerCount];
        for (int i = 0; i < _workerCount; i++)
        {
            int index = i + 1;
            workers[i] = RunConsumerAsync(index, stoppingToken);
        }

        await Task.WhenAll(workers);
    }

    private async Task RunConsumerAsync(int workerNumber, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                InboundEnvelope envelope = await _inboundQueue.DequeueAsync(cancellationToken);
                DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;

                PersistEnvelopeResult persistResult = await _ingestionRepository.PersistEnvelopeAsync(envelope, cancellationToken);
                double persistLatencyMs = (DateTimeOffset.UtcNow - startedAtUtc).TotalMilliseconds;
                _tcpMetrics.RecordPersistLatency(
                    envelope.Port,
                    envelope.Message.Protocol,
                    persistLatencyMs);
                _tcpMetrics.RecordQueueBacklog(
                    envelope.Port,
                    envelope.Message.Protocol,
                    _inboundQueue.Backlog);
                if (persistResult.Status == PersistEnvelopeStatus.Deduplicated)
                {
                    _tcpMetrics.RecordDedupeDrop(envelope.Port, envelope.Message.Protocol);
                }

                if (persistResult.Status != PersistEnvelopeStatus.SkippedUnownedDevice)
                {
                    await PublishCanonicalEventsAsync(envelope, cancellationToken);
                }

                _logger.LogInformation(
                    "queue_consume worker={workerNumber} sessionId={sessionId} packetId={packetId} protocol={protocol} messageType={messageType} imei={imei} backlog={backlog} persistLatencyMs={persistLatencyMs} persistStatus={persistStatus}",
                    workerNumber,
                    envelope.SessionId,
                    envelope.PacketId,
                    envelope.Message.Protocol,
                    envelope.Message.MessageType,
                    envelope.Message.Imei ?? "n/a",
                    _inboundQueue.Backlog,
                    persistLatencyMs,
                    persistResult.Status);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "queue_consume_error worker={workerNumber}", workerNumber);
            }
        }
    }

    private async Task PublishCanonicalEventsAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelope.Message.Imei))
        {
            return;
        }

        string imei = envelope.Message.Imei;
        string telemetryTopic = $"v1/telemetry/{imei}";
        string statusTopic = $"v1/status/{imei}";

        var telemetry = new TelemetryEventV1(
            "1.0",
            Guid.NewGuid(),
            envelope.Message.GpsTimeUtc ?? envelope.Message.ReceivedAtUtc,
            envelope.Message.ReceivedAtUtc,
            imei,
            envelope.Message.Protocol,
            envelope.Message.MessageType,
            envelope.SessionId,
            envelope.PacketId,
            envelope.RemoteIp,
            envelope.Port,
            envelope.Message.GpsTimeUtc,
            envelope.Message.Latitude,
            envelope.Message.Longitude,
            envelope.Message.SpeedKmh,
            envelope.Message.HeadingDeg,
            envelope.PacketId);

        await PublishWithRetryAsync(
            telemetryTopic,
            telemetry,
            envelope,
            "telemetry_v1",
            cancellationToken);

        if (TryBuildStatusEvent(envelope, imei, out DeviceStatusEventV1? statusEvent) &&
            statusEvent is not null)
        {
            await PublishWithRetryAsync(
                statusTopic,
                statusEvent,
                envelope,
                "status_v1",
                cancellationToken);
        }
    }

    private async Task PublishWithRetryAsync<TPayload>(
        string topic,
        TPayload payload,
        InboundEnvelope envelope,
        string eventType,
        CancellationToken cancellationToken)
    {
        int maxRetries = Math.Max(0, _eventBusOptions.MaxPublishRetries);
        int retries = 0;

        while (true)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                if (ShouldSimulatePublishFailure(eventType))
                {
                    throw new InvalidOperationException(
                        $"event_publish_simulated_failure eventType={eventType}");
                }

                await _eventBus.PublishAsync(topic, payload, cancellationToken);
                stopwatch.Stop();
                _tcpMetrics.RecordEventPublishSuccess(
                    envelope.Port,
                    envelope.Message.Protocol,
                    eventType,
                    stopwatch.Elapsed.TotalMilliseconds);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                if (retries >= maxRetries)
                {
                    _tcpMetrics.RecordEventPublishFailure(
                        envelope.Port,
                        envelope.Message.Protocol,
                        eventType);

                    _logger.LogError(
                        ex,
                        "event_publish_failed sessionId={sessionId} packetId={packetId} protocol={protocol} eventType={eventType} topic={topic} retries={retries}",
                        envelope.SessionId,
                        envelope.PacketId,
                        envelope.Message.Protocol,
                        eventType,
                        topic,
                        retries);

                    if (_eventBusOptions.EnableDlq)
                    {
                        await TryPublishDlqAsync(topic, eventType, envelope, ex, retries, cancellationToken);
                    }

                    return;
                }

                retries++;
                _tcpMetrics.RecordEventPublishRetry(
                    envelope.Port,
                    envelope.Message.Protocol,
                    eventType);

                int baseBackoffMs = Math.Max(1, _eventBusOptions.RetryBackoffMs);
                int delayMs = (int)Math.Min(15_000, baseBackoffMs * Math.Pow(2, retries - 1));
                await Task.Delay(delayMs, cancellationToken);
            }
        }
    }

    private bool ShouldSimulatePublishFailure(string eventType)
    {
        if (!_eventBusOptions.EnablePublishFailureSimulation)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_eventBusOptions.SimulateFailureEventType) ||
            !string.Equals(_eventBusOptions.SimulateFailureEventType, eventType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_eventBusOptions.SimulateFailureOnce)
        {
            return _simulatedFailureOnceTracker.TryAdd(eventType, 1);
        }

        return true;
    }

    private async Task TryPublishDlqAsync(
        string failedTopic,
        string eventType,
        InboundEnvelope envelope,
        Exception exception,
        int retryCount,
        CancellationToken cancellationToken)
    {
        string escapedTopic = Uri.EscapeDataString(failedTopic);
        string dlqTopic = $"v1/dlq/{escapedTopic}";
        var dlq = new DlqEnvelopeV1(
            "1.0",
            Guid.NewGuid(),
            failedTopic,
            eventType,
            envelope.Message.Imei,
            envelope.SessionId,
            envelope.PacketId,
            exception.GetType().Name,
            exception.Message,
            DateTimeOffset.UtcNow,
            retryCount);

        try
        {
            await _eventBus.PublishAsync(dlqTopic, dlq, cancellationToken);
            _tcpMetrics.RecordEventDlq(
                envelope.Port,
                envelope.Message.Protocol,
                eventType);
        }
        catch (Exception dlqEx)
        {
            _logger.LogError(
                dlqEx,
                "event_dlq_publish_failed sessionId={sessionId} packetId={packetId} topic={topic}",
                envelope.SessionId,
                envelope.PacketId,
                dlqTopic);
        }
    }

    private static bool TryBuildStatusEvent(
        InboundEnvelope envelope,
        string imei,
        out DeviceStatusEventV1? statusEvent)
    {
        statusEvent = envelope.Message.MessageType switch
        {
            MessageType.Login => new DeviceStatusEventV1(
                "1.0",
                Guid.NewGuid(),
                envelope.Message.ReceivedAtUtc,
                imei,
                DeviceStatusKind.Online,
                envelope.Message.Protocol,
                envelope.Message.MessageType,
                envelope.SessionId,
                envelope.PacketId,
                envelope.RemoteIp,
                envelope.Port,
                "login"),
            MessageType.Heartbeat => new DeviceStatusEventV1(
                "1.0",
                Guid.NewGuid(),
                envelope.Message.ReceivedAtUtc,
                imei,
                DeviceStatusKind.Heartbeat,
                envelope.Message.Protocol,
                envelope.Message.MessageType,
                envelope.SessionId,
                envelope.PacketId,
                envelope.RemoteIp,
                envelope.Port,
                "heartbeat"),
            _ => null
        };

        return statusEvent is not null;
    }
}
