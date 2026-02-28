using ImpiTrack.Tcp.Core.Configuration;
using ImpiTrack.Tcp.Core.Queue;
using Microsoft.Extensions.Options;

namespace TcpServer;

/// <summary>
/// Servicio consumidor en segundo plano que drena envelopes de la cola entrante.
/// </summary>
public sealed class InboundProcessingService : BackgroundService
{
    private readonly ILogger<InboundProcessingService> _logger;
    private readonly IInboundQueue _inboundQueue;
    private readonly int _workerCount;

    /// <summary>
    /// Crea un servicio consumidor de cola usando la concurrencia de workers configurada.
    /// </summary>
    /// <param name="logger">Instancia de logger.</param>
    /// <param name="inboundQueue">Abstraccion de cola entrante.</param>
    /// <param name="options">Opciones del servidor.</param>
    public InboundProcessingService(
        ILogger<InboundProcessingService> logger,
        IInboundQueue inboundQueue,
        IOptions<TcpServerOptions> options)
    {
        _logger = logger;
        _inboundQueue = inboundQueue;
        _workerCount = Math.Max(1, options.Value.Pipeline.ConsumerWorkers);
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
                _logger.LogInformation(
                    "queue_consume worker={workerNumber} sessionId={sessionId} packetId={packetId} protocol={protocol} messageType={messageType} imei={imei} backlog={backlog}",
                    workerNumber,
                    envelope.SessionId,
                    envelope.PacketId,
                    envelope.Message.Protocol,
                    envelope.Message.MessageType,
                    envelope.Message.Imei ?? "n/a",
                    _inboundQueue.Backlog);
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
}
