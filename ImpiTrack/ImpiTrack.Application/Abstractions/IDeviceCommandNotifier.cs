namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Contrato para notificacion en tiempo real del ciclo de vida de comandos de dispositivo.
/// Las implementaciones no deben lanzar excepciones que interrumpan el pipeline de persistencia.
/// </summary>
public interface IDeviceCommandNotifier
{
    /// <summary>
    /// Notifica al usuario propietario que el estado de un comando cambio.
    /// La llamada es best-effort: si falla, el flujo del comando no se ve afectado.
    /// </summary>
    /// <param name="userId">Identificador del usuario propietario del dispositivo.</param>
    /// <param name="command">Estado actual del comando.</param>
    /// <param name="ct">Token de cancelacion.</param>
    Task NotifyStatusChangedAsync(Guid userId, DeviceCommandRecord command, CancellationToken ct);
}
