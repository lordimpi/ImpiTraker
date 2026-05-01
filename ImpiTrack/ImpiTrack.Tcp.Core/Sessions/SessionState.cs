using System.Threading.Channels;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Sessions;

/// <summary>
/// Estado mutable en memoria para una sesion TCP activa.
/// </summary>
public sealed class SessionState
{
    private static readonly BoundedChannelOptions ChannelOpts = new(16)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.Wait
    };

    /// <summary>
    /// Identificador de correlacion de sesion.
    /// </summary>
    public required SessionId SessionId { get; init; }

    /// <summary>
    /// Direccion IP remota del cliente.
    /// </summary>
    public required string RemoteIp { get; init; }

    /// <summary>
    /// Puerto del listener asociado a la sesion.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Marca de tiempo UTC cuando se abrio la sesion.
    /// </summary>
    public required DateTimeOffset ConnectedAtUtc { get; init; }

    /// <summary>
    /// Marca de tiempo UTC del frame mas reciente observado.
    /// </summary>
    public DateTimeOffset LastSeenAtUtc { get; set; }

    /// <summary>
    /// Marca de tiempo UTC del ultimo heartbeat observado.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    /// <summary>
    /// IMEI parseado asociado a la sesion cuando se conoce.
    /// </summary>
    public string? Imei { get; set; }

    /// <summary>
    /// Protocolo detectado para la sesion.
    /// </summary>
    public ProtocolId Protocol { get; set; }

    /// <summary>
    /// Cantidad de frames recibidos en la sesion.
    /// </summary>
    public long FramesIn { get; set; }

    /// <summary>
    /// Cantidad de frames invalidos observados en la sesion.
    /// </summary>
    public long FramesInvalid { get; set; }

    /// <summary>
    /// Motivo de cierre de sesion cuando aplica.
    /// </summary>
    public string? CloseReason { get; set; }

    /// <summary>
    /// Marca de tiempo UTC de cierre de sesion cuando aplica.
    /// </summary>
    public DateTimeOffset? DisconnectedAtUtc { get; set; }

    /// <summary>
    /// Canal de salida acotado para frames salientes (ACKs y comandos).
    /// SingleReader=true (write-loop), SingleWriter=false (read-loop + command service).
    /// </summary>
    public Channel<OutboundFrame> OutboundChannel { get; } = Channel.CreateBounded<OutboundFrame>(ChannelOpts);

    /// <summary>
    /// Indica si la sesion puede recibir comandos: IMEI conocido y canal no completado.
    /// </summary>
    public bool IsCommandable => Imei != null && !OutboundChannel.Reader.Completion.IsCompleted;
}
