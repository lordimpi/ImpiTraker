using System.Collections.Concurrent;
using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Cantrack;

/// <summary>
/// Serializa comandos salientes al formato wire de Cantrack.
/// Formato: *HQ,{IMEI},CMD,{HHMMSS},{BODY}#
/// </summary>
/// <remarks>
/// <para>
/// El campo HHMMSS es la hora UTC actual en formato hhmmss de 6 digitos. Se usa
/// como timestamp de correlacion para el ACK V4 del dispositivo.
/// </para>
/// <para>
/// Colision de hhmmss: si dos comandos para el mismo IMEI se envian en el mismo
/// segundo UTC, el segundo comando obtendria el mismo hhmmss, haciendo imposible
/// correlacionar el ACK correctamente. Para mitigar esto, el serializer mantiene
/// el ultimo hhmmss usado por IMEI e incrementa 1 segundo cuando detecta colision
/// (hasta 5 intentos; TD-3 — configurable por dispositivo diferido).
/// </para>
/// </remarks>
public sealed class CantrackCommandSerializer : IProtocolCommandSerializer
{
    /// <summary>
    /// Password hardcodeado v1. TD-3: mover a configuracion por dispositivo.
    /// </summary>
    private const string CantrackPassword = "654321";

    // Ultimo hhmmss emitido por IMEI. Usado para deteccion y resolucion de colisiones.
    private readonly ConcurrentDictionary<string, string> _lastHhmmss = new(StringComparer.Ordinal);

    // Candado por IMEI para serializar la generacion de hhmmss y evitar race conditions.
    // Se usa un lock global fino ya que la seccion critica es micro (solo formateo de string).
    private readonly object _hhmmssLock = new();

    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Cantrack;

    // Mapa de comandos soportados a sus keywords de wire.
    // SetOverspeedAlarm, SetGeofence, CancelGeofence y RequestSinglePosition
    // NO estan soportados por Cantrack.
    private static readonly Dictionary<DeviceCommandType, string> _keywords = new()
    {
        [DeviceCommandType.Arm]                 = "arm",
        [DeviceCommandType.Disarm]              = "disarm",
        [DeviceCommandType.CutMotor]            = "stop",
        [DeviceCommandType.RestoreMotor]        = "resume",
        [DeviceCommandType.SetMovementAlarm]    = "move",
        [DeviceCommandType.CancelMovementAlarm] = "nomove",
        [DeviceCommandType.CancelAlarm]         = "KC",
        [DeviceCommandType.Reset]               = "reset",
    };

    /// <inheritdoc />
    public bool Supports(DeviceCommandType type) => _keywords.ContainsKey(type);

    /// <inheritdoc />
    public bool AckExpected(DeviceCommandType type) => true;

    /// <inheritdoc />
    /// <exception cref="CommandNotSupportedByProtocolException">
    /// El tipo de comando no esta soportado por Cantrack
    /// (ej: SetOverspeedAlarm, SetGeofence, CancelGeofence, RequestSinglePosition).
    /// </exception>
    /// <exception cref="CommandParameterMissingException">
    /// Falta el parametro 'radius' requerido por SetMovementAlarm.
    /// </exception>
    public ReadOnlyMemory<byte> Serialize(DeviceCommand command)
    {
        if (!_keywords.TryGetValue(command.Type, out string? keyword))
        {
            throw new CommandNotSupportedByProtocolException(command.Type, ProtocolId.Cantrack);
        }

        string body = BuildBody(command, keyword);
        string hhmmss = ReserveHhmmss(command.Imei);
        string wire = $"*HQ,{command.Imei},CMD,{hhmmss},{body}#";

        return Encoding.ASCII.GetBytes(wire);
    }

    // -------------------------------------------------------------------------
    // Body builders
    // -------------------------------------------------------------------------

    private static string BuildBody(DeviceCommand command, string keyword)
    {
        return command.Type switch
        {
            DeviceCommandType.SetMovementAlarm => BuildMovementAlarmBody(command, keyword),
            DeviceCommandType.CancelAlarm      => $"{keyword}{CantrackPassword} 0",
            _                                  => $"{keyword}{CantrackPassword}",
        };
    }

    private static string BuildMovementAlarmBody(DeviceCommand command, string keyword)
    {
        string? rawRadius = command.Parameters?["radius"]?.ToString();
        if (string.IsNullOrWhiteSpace(rawRadius))
        {
            throw new CommandParameterMissingException("radius");
        }

        if (!int.TryParse(rawRadius, out int radius) || radius <= 0)
        {
            throw new ArgumentException(
                $"El parametro 'radius' debe ser un entero positivo. Valor recibido: '{rawRadius}'.",
                nameof(command));
        }

        // Cantrack espera el radio como string de 4 digitos con ceros a la izquierda.
        string paddedRadius = radius.ToString("D4");
        return $"{keyword}{CantrackPassword} {paddedRadius}";
    }

    // -------------------------------------------------------------------------
    // Collision-safe hhmmss reservation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Genera y reserva un hhmmss unico para el IMEI dado, garantizando que el valor
    /// emitido sea siempre estrictamente mayor al ultimo emitido para ese IMEI.
    /// Si el reloj no ha avanzado lo suficiente, se incrementa desde el ultimo valor
    /// emitido hasta 5 veces antes de hacer fallback a la hora actual.
    /// </summary>
    private string ReserveHhmmss(string imei)
    {
        lock (_hhmmssLock)
        {
            string candidate = DateTime.UtcNow.ToString("HHmmss");

            if (_lastHhmmss.TryGetValue(imei, out string? last))
            {
                // Garantia: el valor emitido debe ser > al ultimo. Si el candidato
                // es <= al ultimo (reloj estatico o retroceso de segundo), incrementar
                // desde el ultimo en lugar del candidato.
                if (string.Compare(candidate, last, StringComparison.Ordinal) <= 0)
                {
                    candidate = AddOneSecond(last);
                }
            }

            _lastHhmmss[imei] = candidate;
            return candidate;
        }
    }

    /// <summary>
    /// Incrementa un string hhmmss (formato HHmmss) en 1 segundo con wraparound a medianoche.
    /// </summary>
    private static string AddOneSecond(string hhmmss)
    {
        if (hhmmss.Length != 6 ||
            !int.TryParse(hhmmss.AsSpan(0, 2), out int hours) ||
            !int.TryParse(hhmmss.AsSpan(2, 2), out int minutes) ||
            !int.TryParse(hhmmss.AsSpan(4, 2), out int seconds))
        {
            // Cadena malformada: devolver hora actual como fallback.
            return DateTime.UtcNow.ToString("HHmmss");
        }

        seconds++;
        if (seconds >= 60) { seconds = 0; minutes++; }
        if (minutes >= 60) { minutes = 0; hours++; }
        if (hours >= 24)   { hours = 0; }

        return $"{hours:D2}{minutes:D2}{seconds:D2}";
    }
}
