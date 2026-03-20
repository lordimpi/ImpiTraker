using ImpiTrack.Application.Abstractions;
using ImpiTrack.Shared.Options;

namespace TcpServer;

/// <summary>
/// Hosted service que escanea periodicamente el tracker de presencia para detectar
/// dispositivos que excedieron el umbral de inactividad y notifica su transicion a offline.
/// El timer NO bloquea el pipeline de procesamiento TCP — opera en su propio ciclo independiente.
/// </summary>
public sealed class DevicePresenceMonitor : BackgroundService
{
    private readonly IDevicePresenceTracker _presenceTracker;
    private readonly ITelemetryNotifier _telemetryNotifier;
    private readonly ILogger<DevicePresenceMonitor> _logger;
    private readonly TimeSpan _offlineThreshold;
    private readonly TimeSpan _checkInterval;

    /// <summary>
    /// Crea una instancia del monitor de presencia con configuracion inyectada.
    /// </summary>
    /// <param name="presenceTracker">Tracker de presencia de dispositivos (singleton compartido).</param>
    /// <param name="telemetryNotifier">Notificador de eventos en tiempo real.</param>
    /// <param name="logger">Logger del monitor.</param>
    /// <param name="optionsService">Opciones de presencia de dispositivos.</param>
    public DevicePresenceMonitor(
        IDevicePresenceTracker presenceTracker,
        ITelemetryNotifier telemetryNotifier,
        ILogger<DevicePresenceMonitor> logger,
        IGenericOptionsService<DevicePresenceOptions> optionsService)
    {
        _presenceTracker = presenceTracker;
        _telemetryNotifier = telemetryNotifier;
        _logger = logger;

        DevicePresenceOptions options = optionsService.GetOptions();
        _offlineThreshold = TimeSpan.FromMinutes(Math.Max(1, options.OfflineThresholdMinutes));
        _checkInterval = TimeSpan.FromSeconds(Math.Max(5, options.CheckIntervalSeconds));
    }

    /// <summary>
    /// Ejecuta el ciclo de escaneo periodico hasta la cancelacion del host.
    /// </summary>
    /// <param name="stoppingToken">Token de cancelacion de apagado.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "device_presence_monitor_started offlineThresholdMinutes={OfflineThresholdMinutes} checkIntervalSeconds={CheckIntervalSeconds}",
            _offlineThreshold.TotalMinutes,
            _checkInterval.TotalSeconds);

        using var timer = new PeriodicTimer(_checkInterval);

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
                IReadOnlyList<string> offlineImeis = _presenceTracker.DetectAndRemoveOfflineDevices(_offlineThreshold);

                if (offlineImeis.Count > 0)
                {
                    _logger.LogInformation(
                        "device_presence_offline_detected count={Count}",
                        offlineImeis.Count);
                }

                DateTimeOffset changedAtUtc = DateTimeOffset.UtcNow;

                foreach (string imei in offlineImeis)
                {
                    try
                    {
                        await _telemetryNotifier.NotifyDeviceStatusChangedAsync(
                            imei,
                            "Offline",
                            changedAtUtc,
                            stoppingToken);

                        _logger.LogDebug(
                            "device_presence_offline_notified imei={Imei}",
                            imei);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "device_presence_offline_notify_error imei={Imei}",
                            imei);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "device_presence_monitor_scan_error");
            }
        }

        _logger.LogInformation("device_presence_monitor_stopped");
    }
}
