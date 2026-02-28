namespace ImpiTrack.Tcp.Core.Configuration;

/// <summary>
/// Limites de socket y valores de timeout para sesiones de ingesta TCP.
/// </summary>
public sealed class TcpSocketOptions
{
    /// <summary>
    /// Tamano del buffer de recepcion asignado a sockets del listener.
    /// </summary>
    public int ReceiveBufferBytes { get; set; } = 8192;

    /// <summary>
    /// Maximo de bytes permitidos para un frame decodificado.
    /// </summary>
    public int MaxFrameBytes { get; set; } = 16 * 1024;

    /// <summary>
    /// Timeout para operaciones individuales de lectura de red.
    /// </summary>
    public int ReadTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Periodo maximo de inactividad antes de cerrar la sesion.
    /// </summary>
    public int IdleTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Tiempo permitido para el primer frame valido despues de abrir la conexion.
    /// </summary>
    public int HandshakeTimeoutSeconds { get; set; } = 15;
}
