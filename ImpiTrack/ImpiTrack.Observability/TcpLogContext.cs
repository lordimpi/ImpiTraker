namespace ImpiTrack.Observability;

/// <summary>
/// Nombres canonicos de campos de log estructurado usados por componentes de ingesta TCP.
/// </summary>
public static class TcpLogContext
{
    /// <summary>Campo de id de correlacion de sesion.</summary>
    public const string SessionId = "sessionId";
    /// <summary>Campo de id de correlacion de paquete.</summary>
    public const string PacketId = "packetId";
    /// <summary>Campo de IMEI del dispositivo.</summary>
    public const string Imei = "imei";
    /// <summary>Campo de id de protocolo.</summary>
    public const string Protocol = "protocol";
    /// <summary>Campo de tipo de mensaje.</summary>
    public const string MessageType = "messageType";
    /// <summary>Campo de puerto del listener.</summary>
    public const string Port = "port";
    /// <summary>Campo de IP remota.</summary>
    public const string RemoteIp = "remoteIp";
    /// <summary>Campo de latencia en milisegundos.</summary>
    public const string LatencyMs = "latencyMs";
}
