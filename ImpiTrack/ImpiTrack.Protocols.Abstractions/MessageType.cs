namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Categorias canonicas de mensajes producidas por los parsers de protocolo.
/// </summary>
public enum MessageType
{
    /// <summary>
    /// El mensaje no pudo clasificarse.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Paquete de login o handshake del dispositivo.
    /// </summary>
    Login = 1,

    /// <summary>
    /// Paquete de latido o keep-alive.
    /// </summary>
    Heartbeat = 2,

    /// <summary>
    /// Paquete de posicion o tracking.
    /// </summary>
    Tracking = 3,

    /// <summary>
    /// Paquete de estado o alarma.
    /// </summary>
    Status = 4
}
