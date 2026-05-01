using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Sessions;

/// <summary>
/// Administra ciclo de vida y metadatos de sesiones TCP activas.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Abre y registra una nueva sesion.
    /// </summary>
    /// <param name="remoteIp">Direccion IP remota del cliente.</param>
    /// <param name="port">Puerto del listener.</param>
    /// <returns>Estado de sesion recien creado.</returns>
    SessionState Open(string remoteIp, int port);

    /// <summary>
    /// Actualiza la marca de ultimo visto de una sesion.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    void Touch(SessionId sessionId);

    /// <summary>
    /// Asocia un valor IMEI a una sesion existente.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    /// <param name="imei">IMEI del dispositivo parseado.</param>
    void AttachImei(SessionId sessionId, string? imei);

    /// <summary>
    /// Marca que la sesion recibio un heartbeat valido.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    void MarkHeartbeat(SessionId sessionId);

    /// <summary>
    /// Incrementa el contador de frames recibidos para una sesion.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    void IncrementFramesIn(SessionId sessionId);

    /// <summary>
    /// Incrementa el contador de frames invalidos para una sesion.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    void IncrementFramesInvalid(SessionId sessionId);

    /// <summary>
    /// Asigna un motivo de cierre a una sesion.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    /// <param name="closeReason">Codigo de motivo de cierre.</param>
    void SetCloseReason(SessionId sessionId, string closeReason);

    /// <summary>
    /// Obtiene el estado de sesion por identificador.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    /// <param name="session">Estado de sesion resuelto cuando se encuentra.</param>
    /// <returns><c>true</c> si la sesion existe.</returns>
    bool TryGet(SessionId sessionId, out SessionState? session);

    /// <summary>
    /// Cierra y elimina una sesion.
    /// </summary>
    /// <param name="sessionId">Identificador de sesion.</param>
    /// <returns><c>true</c> si la sesion fue eliminada.</returns>
    bool Close(SessionId sessionId);

    /// <summary>
    /// Busca una sesion activa por IMEI.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo.</param>
    /// <param name="session">Estado de sesion resuelto cuando se encuentra.</param>
    /// <returns><c>true</c> si existe una sesion activa para el IMEI.</returns>
    bool TryGetByImei(string imei, out SessionState? session);

    /// <summary>
    /// Intenta encolar un frame de comando en el canal de salida de la sesion asociada al IMEI.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo destino.</param>
    /// <param name="frame">Frame de comando a encolar.</param>
    /// <returns>
    /// <c>true</c> si el frame fue encolado; <c>false</c> si el IMEI no tiene sesion activa,
    /// la sesion esta cerrada, el canal esta completado, o el canal esta lleno.
    /// </returns>
    bool TryEnqueueCommand(string imei, OutboundFrame frame);

    /// <summary>
    /// Se dispara luego de que un IMEI es asociado exitosamente a una sesion.
    /// Usado por <c>OutboundCommandReconnectService</c> para re-inyectar comandos pendientes.
    /// </summary>
    event Action<SessionState>? ImeiAttached;
}
