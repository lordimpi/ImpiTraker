using System.Collections.Concurrent;
using ImpiTrack.Shared.Options;

namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Implementacion in-memory thread-safe del tracker de presencia de dispositivos.
/// Usa un ConcurrentDictionary para almacenar el ultimo timestamp de actividad por IMEI.
/// Registrado como singleton para compartir estado entre workers consumidores y el monitor de presencia.
/// </summary>
public sealed class DevicePresenceTracker : IDevicePresenceTracker
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSeen = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _offlineThreshold;

    /// <summary>
    /// Crea una instancia del tracker de presencia con el umbral de offline configurado.
    /// </summary>
    /// <param name="optionsService">Servicio de opciones para DevicePresenceOptions.</param>
    public DevicePresenceTracker(IGenericOptionsService<DevicePresenceOptions> optionsService)
    {
        DevicePresenceOptions options = optionsService.GetOptions();
        _offlineThreshold = TimeSpan.FromMinutes(Math.Max(1, options.OfflineThresholdMinutes));
    }

    /// <inheritdoc />
    public bool RecordActivity(string imei)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool isNewlyOnline = false;

        _lastSeen.AddOrUpdate(
            imei,
            addValueFactory: _ =>
            {
                isNewlyOnline = true;
                return now;
            },
            updateValueFactory: (_, previous) =>
            {
                if (now - previous > _offlineThreshold)
                {
                    isNewlyOnline = true;
                }

                return now;
            });

        return isNewlyOnline;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> DetectAndRemoveOfflineDevices(TimeSpan threshold)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - threshold;
        var offlineImeis = new List<string>();

        foreach (var kvp in _lastSeen)
        {
            if (kvp.Value <= cutoff)
            {
                if (_lastSeen.TryRemove(kvp.Key, out _))
                {
                    offlineImeis.Add(kvp.Key);
                }
            }
        }

        return offlineImeis;
    }
}
