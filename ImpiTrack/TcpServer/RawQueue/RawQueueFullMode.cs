namespace TcpServer.RawQueue;

/// <summary>
/// Politicas soportadas para manejo de capacidad maxima de cola raw.
/// </summary>
public enum RawQueueFullMode
{
    /// <summary>
    /// Espera asincronamente hasta que exista espacio disponible.
    /// </summary>
    Wait = 1,

    /// <summary>
    /// Descarta el nuevo elemento cuando la cola esta llena.
    /// </summary>
    Drop = 2,

    /// <summary>
    /// Descarta el nuevo elemento y solicita cierre de sesion.
    /// </summary>
    Disconnect = 3
}
