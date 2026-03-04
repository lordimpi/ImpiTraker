using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.IOptionPattern;
using ImpiTrack.Observability;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Configuration;
using TcpServer.RawQueue;

namespace TcpServer;

/// <summary>
/// Consumidor en segundo plano para persistencia diferida de paquetes raw.
/// </summary>
public sealed class RawPacketProcessingService : BackgroundService
{
    private readonly ILogger<RawPacketProcessingService> _logger;
    private readonly IRawPacketQueue _rawQueue;
    private readonly IIngestionRepository _ingestionRepository;
    private readonly ITcpMetrics _tcpMetrics;
    private readonly int _workerCount;

    /// <summary>
    /// Crea el servicio de procesamiento de cola raw.
    /// </summary>
    /// <param name="logger">Logger estructurado.</param>
    /// <param name="rawQueue">Cola raw acotada.</param>
    /// <param name="ingestionRepository">Repositorio de ingesta.</param>
    /// <param name="tcpMetrics">Publicador de metricas TCP.</param>
    /// <param name="optionsService">Opciones del servidor TCP.</param>
    public RawPacketProcessingService(
        ILogger<RawPacketProcessingService> logger,
        IRawPacketQueue rawQueue,
        IIngestionRepository ingestionRepository,
        ITcpMetrics tcpMetrics,
        IGenericOptionsService<TcpServerOptions> optionsService)
    {
        _logger = logger;
        _rawQueue = rawQueue;
        _ingestionRepository = ingestionRepository;
        _tcpMetrics = tcpMetrics;
        _workerCount = Math.Max(1, optionsService.GetOptions().Pipeline.RawConsumerWorkers);
    }

    /// <summary>
    /// Arranca workers raw hasta el apagado del host.
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
                RawPacketEnvelope envelope = await _rawQueue.DequeueAsync(cancellationToken);
                await _ingestionRepository.AddRawPacketAsync(
                    envelope.Record,
                    envelope.InboundBacklog,
                    cancellationToken);

                _tcpMetrics.RecordRawQueueBacklog(
                    envelope.Record.Port,
                    envelope.Record.Protocol,
                    _rawQueue.Backlog);

                _logger.LogDebug(
                    "raw_queue_consume worker={worker} sessionId={sessionId} packetId={packetId} protocol={protocol} port={port} backlog={backlog}",
                    workerNumber,
                    envelope.Record.SessionId,
                    envelope.Record.PacketId,
                    envelope.Record.Protocol,
                    envelope.Record.Port,
                    _rawQueue.Backlog);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "raw_queue_consume_error worker={worker}", workerNumber);
            }
        }
    }
}
