namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Opciones de configuracion para el sistema de comandos salientes a dispositivos GPS.
/// Seccion: "DeviceCommands" en appsettings.json.
/// </summary>
public sealed class DeviceCommandsOptions
{
    /// <summary>
    /// Nombre de seccion en la configuracion de la aplicacion.
    /// </summary>
    public const string SectionName = "DeviceCommands";

    /// <summary>
    /// Segundos de espera por un ACK antes de marcar un comando Sent como Timeout.
    /// Valor por defecto: 60.
    /// </summary>
    public int AckTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Intervalo en segundos entre iteraciones del escaneo de timeouts del BackgroundService.
    /// Valor por defecto: 30.
    /// </summary>
    public int TimeoutScanIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Al arrancar la API, comandos en estado Sent con antiguedad mayor a este umbral se marcan como InterruptedByRestart.
    /// Valor por defecto: 5 minutos.
    /// </summary>
    public int StartupRecoveryAgeMinutes { get; set; } = 5;

    /// <summary>
    /// Limite de rate por usuario: maximo de comandos encolados por minuto por usuario.
    /// Valor por defecto: 10.
    /// </summary>
    public int RateLimitPerUserPerMinute { get; set; } = 10;

    /// <summary>
    /// Contrasena GPRS por defecto para dispositivos Cantrack.
    /// v1 es hardcodeada; TD-3 permitira configuracion per-dispositivo en el futuro.
    /// </summary>
    public string CantrackDefaultPassword { get; set; } = "654321";

    /// <summary>
    /// Contrasena GPRS por defecto para dispositivos Coban.
    /// Coban tipicamente no requiere contrasena; mantenida para compatibilidad futura.
    /// </summary>
    public string CobanDefaultPassword { get; set; } = "";
}
