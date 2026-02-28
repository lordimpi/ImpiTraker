namespace ImpiTrack.Tcp.Core.Configuration;

/// <summary>
/// Configuracion de seguridad operativa para control de abuso TCP por IP.
/// </summary>
public sealed class TcpSecurityOptions
{
    /// <summary>
    /// Maximo de frames permitidos por minuto para una misma IP.
    /// </summary>
    public int MaxFramesPerMinutePerIp { get; set; } = 600;

    /// <summary>
    /// Maximo de frames invalidos permitidos por minuto para una misma IP.
    /// </summary>
    public int InvalidFrameThreshold { get; set; } = 40;

    /// <summary>
    /// Minutos de bloqueo temporal cuando una IP supera umbrales.
    /// </summary>
    public int BanMinutes { get; set; } = 15;
}
