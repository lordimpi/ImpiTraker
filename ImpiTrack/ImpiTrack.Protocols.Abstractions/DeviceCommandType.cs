namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Tipos de comandos salientes soportados por el pipeline de dispositivos.
/// </summary>
public enum DeviceCommandType
{
    /// <summary>
    /// Activa el modo armado del dispositivo.
    /// </summary>
    Arm = 1,

    /// <summary>
    /// Desactiva el modo armado del dispositivo.
    /// </summary>
    Disarm = 2,

    /// <summary>
    /// Corta el motor del vehiculo.
    /// </summary>
    CutMotor = 3,

    /// <summary>
    /// Restaura el motor del vehiculo.
    /// </summary>
    RestoreMotor = 4,

    /// <summary>
    /// Activa la alarma de movimiento.
    /// </summary>
    SetMovementAlarm = 5,

    /// <summary>
    /// Cancela la alarma de movimiento.
    /// </summary>
    CancelMovementAlarm = 6,

    /// <summary>
    /// Activa la alarma de velocidad excesiva.
    /// </summary>
    SetOverspeedAlarm = 7,

    /// <summary>
    /// Define una geocerca activa.
    /// </summary>
    SetGeofence = 8,

    /// <summary>
    /// Cancela la geocerca activa.
    /// </summary>
    CancelGeofence = 9,

    /// <summary>
    /// Cancela todas las alarmas activas.
    /// </summary>
    CancelAlarm = 10,

    /// <summary>
    /// Solicita una posicion GPS unica inmediata.
    /// </summary>
    RequestSinglePosition = 11,

    /// <summary>
    /// Reinicia el dispositivo.
    /// </summary>
    Reset = 12
}
