using ImpiTrack.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace TcpServer.BackgroundServices;

/// <summary>
/// Servicio one-shot de arranque que detecta comandos en estado <c>Sent</c> anteriores al
/// umbral configurado y los transiciona a <c>InterruptedByRestart</c>.
/// Cubre el caso donde el host se reinicio mientras habia comandos esperando ACK.
/// </summary>
public sealed class DeviceCommandStartupRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DeviceCommandsOptions> _options;
    private readonly ILogger<DeviceCommandStartupRecoveryService> _logger;

    /// <summary>
    /// Crea el servicio de recuperacion de arranque.
    /// </summary>
    /// <param name="scopeFactory">Factory para crear scopes DI transitorios.</param>
    /// <param name="options">Opciones de configuracion de comandos.</param>
    /// <param name="logger">Logger estructurado.</param>
    public DeviceCommandStartupRecoveryService(
        IServiceScopeFactory scopeFactory,
        IOptions<DeviceCommandsOptions> options,
        ILogger<DeviceCommandStartupRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        TimeSpan recoveryAge = TimeSpan.FromMinutes(_options.Value.StartupRecoveryAgeMinutes);
        DateTimeOffset threshold = DateTimeOffset.UtcNow - recoveryAge;

        _logger.LogInformation(
            "device_command_startup_recovery_started recoveryAgeMinutes={RecoveryAgeMinutes} threshold={Threshold:O}",
            _options.Value.StartupRecoveryAgeMinutes,
            threshold);

        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            IDeviceCommandRepository repo = scope.ServiceProvider.GetRequiredService<IDeviceCommandRepository>();
            IDeviceCommandNotifier notifier = scope.ServiceProvider.GetRequiredService<IDeviceCommandNotifier>();

            IReadOnlyList<DeviceCommandRecord> staleCommands = await repo.ScanStaleSentAsync(threshold, cancellationToken);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            int recovered = 0;

            foreach (DeviceCommandRecord cmd in staleCommands)
            {
                try
                {
                    bool updated = await repo.UpdateStatusAsync(
                        cmd.CommandId,
                        newStatus: "InterruptedByRestart",
                        timestamp: now,
                        payloadSent: null,
                        correlationKey: null,
                        correlationTimestamp: null,
                        responseCode: null,
                        responseText: null,
                        failureReason: "interrupted_by_restart",
                        cancellationToken);

                    if (!updated)
                    {
                        _logger.LogDebug(
                            "device_command_startup_recovery_skip commandId={CommandId} (already terminal)",
                            cmd.CommandId);
                        continue;
                    }

                    recovered++;
                    cmd.Status = "InterruptedByRestart";
                    cmd.FailureReason = "interrupted_by_restart";

                    try
                    {
                        await notifier.NotifyStatusChangedAsync(cmd.UserId, cmd, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // Best-effort: SignalR may have no listeners at startup; do not fail recovery.
                        _logger.LogWarning(
                            ex,
                            "device_command_startup_recovery_notify_error commandId={CommandId}",
                            cmd.CommandId);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "device_command_startup_recovery_row_error commandId={CommandId}",
                        cmd.CommandId);
                }
            }

            _logger.LogInformation(
                "device_command_startup_recovery_completed count={Count}",
                recovered);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("device_command_startup_recovery_cancelled");
        }
        catch (Exception ex)
        {
            // Do not let startup recovery crash the host — log and continue.
            _logger.LogError(ex, "device_command_startup_recovery_error");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
