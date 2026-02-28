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
}
