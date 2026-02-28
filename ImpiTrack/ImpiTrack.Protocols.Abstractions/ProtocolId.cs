namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Representa los protocolos de red soportados por el pipeline TCP.
/// </summary>
public enum ProtocolId
{
    /// <summary>
    /// Protocolo desconocido o no resuelto.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Familia de protocolo Coban/TK.
    /// </summary>
    Coban = 1,

    /// <summary>
    /// Familia de protocolo Cantrack.
    /// </summary>
    Cantrack = 2
}
