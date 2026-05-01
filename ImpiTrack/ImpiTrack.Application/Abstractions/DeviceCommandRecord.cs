namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Representacion mutable del registro de un comando de dispositivo para hidratacion con Dapper.
/// Mapea directamente a las columnas de la tabla <c>device_commands</c>.
/// </summary>
public sealed class DeviceCommandRecord
{
    /// <summary>Identificador unico del comando.</summary>
    public Guid CommandId { get; set; }

    /// <summary>IMEI del dispositivo destino.</summary>
    public string Imei { get; set; } = string.Empty;

    /// <summary>Identificador del usuario que creo el comando.</summary>
    public Guid UserId { get; set; }

    /// <summary>Tipo de comando como cadena (nombre del enum <c>DeviceCommandType</c>).</summary>
    public string CommandType { get; set; } = string.Empty;

    /// <summary>Parametros del comando serializados como JSON. Null si no aplica.</summary>
    public string? Parameters { get; set; }

    /// <summary>Protocolo del dispositivo al momento de la creacion del comando.</summary>
    public string? Protocol { get; set; }

    /// <summary>Payload exacto enviado al dispositivo (texto del frame wire). Null hasta que sea enviado.</summary>
    public string? PayloadSent { get; set; }

    /// <summary>Clave de correlacion para asociar el ACK con este comando (keyword Coban o cmd Cantrack).</summary>
    public string? CorrelationKey { get; set; }

    /// <summary>Timestamp de correlacion exacto para protocolos que lo soportan (hhmmss Cantrack).</summary>
    public string? CorrelationTimestamp { get; set; }

    /// <summary>Estado actual del ciclo de vida: Queued, Sent, Acknowledged, Failed, Timeout, InterruptedByRestart.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Marca de tiempo UTC en que el comando fue encolado.</summary>
    public DateTimeOffset QueuedAtUtc { get; set; }

    /// <summary>Marca de tiempo UTC en que el comando fue enviado al dispositivo. Null hasta ese momento.</summary>
    public DateTimeOffset? SentAtUtc { get; set; }

    /// <summary>Marca de tiempo UTC en que se recibio el ACK del dispositivo. Null hasta ese momento.</summary>
    public DateTimeOffset? AckedAtUtc { get; set; }

    /// <summary>Codigo de respuesta devuelto por el dispositivo en el ACK. Null hasta que sea recibido.</summary>
    public string? ResponseCode { get; set; }

    /// <summary>Texto de respuesta completo del dispositivo. Null hasta que sea recibido.</summary>
    public string? ResponseText { get; set; }

    /// <summary>Razon del fallo cuando el status es Failed, Timeout o InterruptedByRestart. Null en casos exitosos.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Direccion IP del cliente HTTP que creo el comando. Null si no disponible.</summary>
    public string? UserIp { get; set; }

    /// <summary>User-Agent HTTP del cliente que creo el comando. Null si no disponible.</summary>
    public string? UserAgent { get; set; }
}
