namespace ImpiTrack.Tcp.Core.Configuration;

/// <summary>
/// Representa un endpoint TCP configurado del listener.
/// </summary>
public sealed class TcpServerEndpointOptions
{
    /// <summary>
    /// Nombre descriptivo del listener usado en logs.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Puerto TCP a enlazar.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Protocolo preferido para el endpoint (por ejemplo COBAN o CANTRACK).
    /// </summary>
    public string Protocol { get; set; } = "Unknown";
}
