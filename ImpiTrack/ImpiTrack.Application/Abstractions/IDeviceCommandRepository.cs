namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Operaciones de persistencia para el ciclo de vida de comandos de dispositivo.
/// </summary>
public interface IDeviceCommandRepository
{
    /// <summary>
    /// Inserta un nuevo comando en estado <c>Queued</c>.
    /// </summary>
    /// <param name="record">Registro completo del comando a persistir.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Identificador unico del comando insertado.</returns>
    Task<Guid> InsertAsync(DeviceCommandRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Actualiza el estado del comando junto con los campos opcionales de transicion.
    /// La implementacion verifica que el estado actual no sea terminal antes de ejecutar el UPDATE.
    /// </summary>
    /// <param name="commandId">Identificador del comando a actualizar.</param>
    /// <param name="newStatus">Nuevo estado del ciclo de vida.</param>
    /// <param name="timestamp">Timestamp de transicion (sent_at_utc o acked_at_utc segun el estado).</param>
    /// <param name="payloadSent">Payload wire enviado al dispositivo. Null si no aplica en esta transicion.</param>
    /// <param name="correlationKey">Clave de correlacion persistida al enviar. Null si no aplica.</param>
    /// <param name="correlationTimestamp">Timestamp de correlacion (hhmmss). Null si no aplica.</param>
    /// <param name="responseCode">Codigo de respuesta del dispositivo. Null si no aplica.</param>
    /// <param name="responseText">Texto completo de respuesta. Null si no aplica.</param>
    /// <param name="failureReason">Codigo corto de razon de fallo. Null si no aplica.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns><c>true</c> si la fila fue actualizada; <c>false</c> si el estado era terminal o el comando no existe.</returns>
    Task<bool> UpdateStatusAsync(
        Guid commandId,
        string newStatus,
        DateTimeOffset? timestamp,
        string? payloadSent,
        string? correlationKey,
        string? correlationTimestamp,
        string? responseCode,
        string? responseText,
        string? failureReason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene un comando por su identificador unico.
    /// </summary>
    /// <param name="commandId">Identificador del comando.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Registro del comando o null si no existe.</returns>
    Task<DeviceCommandRecord?> GetByIdAsync(Guid commandId, CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene todos los comandos en estado <c>Queued</c> para un IMEI dado,
    /// ordenados por <c>queued_at_utc ASC</c> para entrega FIFO al reconectar.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<IReadOnlyList<DeviceCommandRecord>> GetQueuedByImeiAsync(string imei, CancellationToken cancellationToken);

    /// <summary>
    /// Lista comandos de un dispositivo con paginacion y filtros opcionales.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="userId">Identificador del usuario propietario (aplicado como filtro de seguridad).</param>
    /// <param name="query">Parametros de paginacion y filtrado.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<PagedResult<DeviceCommandRecord>> ListPagedAsync(
        string imei,
        Guid userId,
        DeviceCommandListQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene comandos en estado <c>Sent</c> cuyo <c>sent_at_utc</c> es anterior a <paramref name="olderThan"/>.
    /// Usado por el servicio de timeout para detectar comandos sin ACK.
    /// </summary>
    /// <param name="olderThan">Umbral UTC de antiguedad; se retornan comandos enviados antes de este momento.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task<IReadOnlyList<DeviceCommandRecord>> ScanStaleSentAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);

    /// <summary>
    /// Busca el comando <c>Sent</c> mas antiguo que coincide con la clave de correlacion del IMEI.
    /// Implementa la semantica FIFO de Coban (un keyword puede corresponder al mas antiguo pendiente).
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="correlationKey">Keyword de correlacion Coban.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Comando mas antiguo en estado Sent con esa clave, o null si no hay coincidencia.</returns>
    Task<DeviceCommandRecord?> MatchPendingByKeywordAsync(string imei, string correlationKey, CancellationToken cancellationToken);

    /// <summary>
    /// Busca el comando <c>Sent</c> que coincide exactamente con la clave y el timestamp de correlacion.
    /// Implementa la semantica de correlacion exacta de Cantrack (CMD + hhmmss del frame V4).
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="correlationKey">CMD del frame V4 Cantrack.</param>
    /// <param name="correlationTimestamp">hhmmss del frame V4 Cantrack.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Comando con coincidencia exacta o null si no existe.</returns>
    Task<DeviceCommandRecord?> MatchPendingByCorrelationAsync(
        string imei,
        string correlationKey,
        string correlationTimestamp,
        CancellationToken cancellationToken);
}
