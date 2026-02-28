using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Observability;

/// <summary>
/// Contrato para publicar metricas tecnicas del pipeline TCP.
/// </summary>
public interface ITcpMetrics
{
    /// <summary>
    /// Registra apertura de conexion por puerto.
    /// </summary>
    /// <param name="port">Puerto del listener.</param>
    void RecordConnectionOpened(int port);

    /// <summary>
    /// Registra cierre de conexion por puerto.
    /// </summary>
    /// <param name="port">Puerto del listener.</param>
    void RecordConnectionClosed(int port);

    /// <summary>
    /// Registra recepcion de frame por puerto y protocolo.
    /// </summary>
    /// <param name="port">Puerto del listener.</param>
    /// <param name="protocol">Protocolo resuelto del frame.</param>
    void RecordFrameReceived(int port, ProtocolId protocol);

    /// <summary>
    /// Registra frame invalido por puerto y protocolo.
    /// </summary>
    /// <param name="port">Puerto del listener.</param>
    /// <param name="protocol">Protocolo resuelto del frame.</param>
    void RecordFrameInvalid(int port, ProtocolId protocol);

    /// <summary>
    /// Registra resultado de parseo para un frame.
    /// </summary>
    /// <param name="port">Puerto del listener.</param>
    /// <param name="protocol">Protocolo asociado.</param>
    /// <param name="success">Indica si el parseo fue exitoso.</param>
    void RecordParseResult(int port, ProtocolId protocol, bool success);

    /// <summary>
    /// Registra envio de ACK y su latencia.
    /// </summary>
    /// <param name="port">Puerto del listener.</param>
    /// <param name="protocol">Protocolo del mensaje.</param>
    /// <param name="latencyMs">Latencia de envio del ACK en milisegundos.</param>
    void RecordAck(int port, ProtocolId protocol, double latencyMs);
}
