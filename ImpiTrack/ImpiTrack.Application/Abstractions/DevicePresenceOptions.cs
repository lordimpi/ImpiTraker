namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Opciones de configuracion para el sistema de deteccion de presencia de dispositivos.
/// Seccion: "DevicePresence" en appsettings.json.
/// </summary>
public sealed class DevicePresenceOptions
{
    /// <summary>
    /// Nombre de seccion en la configuracion de la aplicacion.
    /// </summary>
    public const string SectionName = "DevicePresence";

    /// <summary>
    /// Minutos de inactividad antes de considerar un dispositivo como offline.
    /// Valor por defecto: 10 minutos.
    /// </summary>
    public int OfflineThresholdMinutes { get; set; } = 10;

    /// <summary>
    /// Intervalo en segundos entre cada escaneo de dispositivos offline.
    /// Valor por defecto: 60 segundos.
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 60;
}
