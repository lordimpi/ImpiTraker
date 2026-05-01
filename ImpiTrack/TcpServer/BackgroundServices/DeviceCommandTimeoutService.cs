using ImpiTrack.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace TcpServer.BackgroundServices;

/// <summary>
/// Servicio periodico que escanea comandos en estado <c>Sent</c> cuyo ACK no fue recibido
/// dentro del umbral configurado y los transiciona a <c>Timeout</c>.
/// Opera en su propio ciclo independiente sin bloquear el pipeline TCP.
/// </summary>
public sealed class DeviceCommandTimeoutService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DeviceCommandsOptions> _options;
    private readonly ILogger<DeviceCommandTimeoutService> _logger;

    /// <summary>
    /// Crea el servicio de timeout de comandos.
    /// </summary>
    /// <param name="scopeFactory">Factory para crear scopes DI transitorios.</param>
    /// <param name="options">Opciones de configuracion de comandos.</param>
    /// <param name="logger">Logger estructurado.</param>
    public DeviceCommandTimeoutService(
        IServiceScopeFactory scopeFactory,
        IOptions<DeviceCommandsOptions> options,
        ILogger<DeviceCommandTimeoutService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(_options.Value.TimeoutScanIntervalSeconds);
        TimeSpan threshold = TimeSpan.FromSeconds(_options.Value.AckTimeoutSeconds);

        _logger.LogInformation(
            "device_command_timeout_service_started ackTimeoutSeconds={AckTimeoutSeconds} scanIntervalSeconds={ScanIntervalSeconds}",
            _options.Value.AckTimeoutSeconds,
            _options.Value.TimeoutScanIntervalSeconds);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await ScanAndTimeoutStaleCommandsAsync(threshold, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "device_command_timeout_service_scan_error");
            }
        }

        _logger.LogInformation("device_command_timeout_service_stopped");
    }

    private async Task ScanAndTimeoutStaleCommandsAsync(TimeSpan threshold, CancellationToken stoppingToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IDeviceCommandRepository repo = scope.ServiceProvider.GetRequiredService<IDeviceCommandRepository>();
        IDeviceCommandNotifier notifier = scope.ServiceProvider.GetRequiredService<IDeviceCommandNotifier>();

        DateTimeOffset olderThan = DateTimeOffset.UtcNow - threshold;
        IReadOnlyList<DeviceCommandRecord> staleCommands = await repo.ScanStaleSentAsync(olderThan, stoppingToken);

        if (staleCommands.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "device_command_timeout_service_stale_found count={Count}",
            staleCommands.Count);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (DeviceCommandRecord cmd in staleCommands)
        {
            try
            {
                bool updated = await repo.UpdateStatusAsync(
                    cmd.CommandId,
                    newStatus: "Timeout",
                    timestamp: now,
                    payloadSent: null,
                    correlationKey: null,
                    correlationTimestamp: null,
                    responseCode: null,
                    responseText: null,
                    failureReason: "ack_timeout",
                    stoppingToken);

                if (!updated)
                {
                    _logger.LogDebug(
                        "device_command_timeout_service_skip commandId={CommandId} (already terminal)",
                        cmd.CommandId);
                    continue;
                }

                cmd.Status = "Timeout";
                cmd.FailureReason = "ack_timeout";

                try
                {
                    await notifier.NotifyStatusChangedAsync(cmd.UserId, cmd, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "device_command_timeout_service_notify_error commandId={CommandId}",
                        cmd.CommandId);
                }

                _logger.LogInformation(
                    "device_command_timeout commandId={CommandId} imei={Imei}",
                    cmd.CommandId,
                    cmd.Imei);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "device_command_timeout_service_row_error commandId={CommandId}",
                    cmd.CommandId);
            }
        }
    }
}
