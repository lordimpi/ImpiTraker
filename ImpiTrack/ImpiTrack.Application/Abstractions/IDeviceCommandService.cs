using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Servicio de aplicacion para el ciclo de vida de comandos de dispositivo:
/// orquesta la validacion, persistencia, despacho al canal de salida y procesamiento de ACKs.
/// </summary>
public interface IDeviceCommandService
{
    /// <summary>
    /// Encola un comando para el dispositivo indicado: valida ownership, resuelve protocolo,
    /// serializa el payload y persiste el registro en estado <c>Queued</c> (o <c>Sent</c> si
    /// el dispositivo esta online y el canal acepta el frame).
    /// </summary>
    /// <param name="imei">IMEI del dispositivo destino.</param>
    /// <param name="userId">Identificador del usuario solicitante (debe ser propietario del IMEI).</param>
    /// <param name="type">Tipo de comando.</param>
    /// <param name="parameters">Parametros opcionales del comando.</param>
    /// <param name="confirm">Marca de confirmacion explicita para comandos peligrosos (CutMotor, Reset).</param>
    /// <param name="userIp">Direccion IP del cliente HTTP que origina la peticion.</param>
    /// <param name="userAgent">User-Agent del cliente HTTP.</param>
    /// <param name="ct">Token de cancelacion.</param>
    /// <returns>Resultado con el commandId si la operacion fue exitosa, o codigo de error si fallo.</returns>
    Task<EnqueueResult> EnqueueAsync(
        string imei,
        Guid userId,
        DeviceCommandType type,
        IDictionary<string, object>? parameters,
        bool confirm,
        string userIp,
        string userAgent,
        CancellationToken ct);

    /// <summary>
    /// Obtiene un comando por su identificador, validando que pertenezca al usuario indicado.
    /// </summary>
    /// <param name="commandId">Identificador unico del comando.</param>
    /// <param name="userId">Identificador del usuario propietario.</param>
    /// <param name="ct">Token de cancelacion.</param>
    /// <returns>Registro del comando o null si no existe o el usuario no es propietario.</returns>
    Task<DeviceCommandRecord?> GetByIdAsync(Guid commandId, Guid userId, CancellationToken ct);

    /// <summary>
    /// Lista los comandos del IMEI propiedad del usuario, con paginacion y filtros opcionales.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="userId">Identificador del usuario propietario.</param>
    /// <param name="query">Parametros de paginacion y filtrado.</param>
    /// <param name="ct">Token de cancelacion.</param>
    /// <returns>Pagina de registros del comando.</returns>
    Task<PagedResult<DeviceCommandRecord>> ListAsync(
        string imei,
        Guid userId,
        DeviceCommandListQuery query,
        CancellationToken ct);

    /// <summary>
    /// Procesa un ACK recibido del dispositivo: realiza la correlacion segun protocolo,
    /// actualiza el estado del comando correspondiente y emite la notificacion al usuario.
    /// Si no hay coincidencia con un comando pendiente, registra y descarta silenciosamente.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo que reporta el ACK.</param>
    /// <param name="protocol">Protocolo de la sesion que origino el ACK.</param>
    /// <param name="responseCode">Codigo de respuesta extraido del ACK.</param>
    /// <param name="correlationKey">Clave de correlacion (keyword Coban o cmd Cantrack). Null si no aplica.</param>
    /// <param name="correlationTimestamp">Timestamp de correlacion (hhmmss Cantrack). Null si no aplica.</param>
    /// <param name="ct">Token de cancelacion.</param>
    Task HandleAckAsync(
        string imei,
        ProtocolId protocol,
        string responseCode,
        string? correlationKey,
        string? correlationTimestamp,
        CancellationToken ct);

    /// <summary>
    /// Transiciona el comando de <c>Queued</c> a <c>Sent</c> persistiendo
    /// <c>sent_at_utc=now</c>, el <c>payload_sent</c> exacto y las claves de correlacion
    /// efectivamente despachadas. Es el write-loop del worker quien debe invocar este
    /// metodo TRAS el <c>stream.WriteAsync</c> exitoso (REQ-LC-3, REQ-AUDIT-1).
    /// Emite la notificacion <c>CommandStatusChanged</c> con estado <c>Sent</c> en best-effort.
    /// </summary>
    /// <param name="commandId">Identificador del comando despachado.</param>
    /// <param name="payloadSent">Bytes serializados (decodificados a texto) que se escribieron al stream.</param>
    /// <param name="correlationKey">Clave de correlacion del payload despachado (keyword Coban / cmd Cantrack).</param>
    /// <param name="correlationTimestamp">Timestamp de correlacion (hhmmss Cantrack) o null para Coban.</param>
    /// <param name="ct">Token de cancelacion.</param>
    Task MarkSentAsync(
        Guid commandId,
        string payloadSent,
        string? correlationKey,
        string? correlationTimestamp,
        CancellationToken ct);
}

/// <summary>
/// Resultado de la operacion <see cref="IDeviceCommandService.EnqueueAsync"/>.
/// </summary>
/// <param name="Success">Indica si el comando fue persistido exitosamente.</param>
/// <param name="CommandId">Identificador del comando creado, cuando <see cref="Success"/> es <c>true</c>.</param>
/// <param name="ErrorCode">
/// Codigo de error normalizado cuando la operacion fallo. Valores posibles:
/// <c>not_found</c> (IMEI no pertenece al usuario),
/// <c>protocol_unknown</c> (no fue posible resolver el protocolo del dispositivo),
/// <c>command_not_supported_by_protocol</c> (el protocolo no soporta el tipo de comando),
/// <c>confirmation_required</c> (comando peligroso sin <c>confirm=true</c>),
/// <c>invalid_parameters</c> (parametros faltantes o fuera de rango),
/// <c>device_offline</c> (informativo cuando se persiste como Queued porque el dispositivo no esta online).
/// </param>
/// <param name="ErrorMessage">Mensaje legible del error. Null si la operacion fue exitosa.</param>
public sealed record EnqueueResult(
    bool Success,
    Guid? CommandId,
    string? ErrorCode,
    string? ErrorMessage);
