namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Contrato para el tracking de presencia (online/offline) de dispositivos GPS.
/// Las implementaciones deben ser thread-safe ya que multiples workers consumidores
/// invocan RecordActivity de forma concurrente.
/// </summary>
public interface IDevicePresenceTracker
{
    /// <summary>
    /// Registra actividad recibida de un dispositivo y determina si paso a estado online.
    /// Un dispositivo se considera "newly online" cuando no estaba trackeado previamente
    /// o cuando su ultimo paquete fue recibido hace mas de el umbral de offline configurado.
    /// </summary>
    /// <param name="imei">IMEI del dispositivo que reporto actividad.</param>
    /// <returns>
    /// <c>true</c> si el dispositivo acaba de transicionar a online (primera actividad
    /// o reactivacion despues de timeout); <c>false</c> si ya estaba online.
    /// </returns>
    bool RecordActivity(string imei);

    /// <summary>
    /// Obtiene los IMEIs de dispositivos cuyo ultimo paquete fue recibido hace mas del umbral indicado
    /// y los remueve del tracking interno (transicion a offline).
    /// </summary>
    /// <param name="threshold">Duracion maxima de inactividad antes de considerar offline.</param>
    /// <returns>Lista de IMEIs que pasaron a offline.</returns>
    IReadOnlyList<string> DetectAndRemoveOfflineDevices(TimeSpan threshold);
}
