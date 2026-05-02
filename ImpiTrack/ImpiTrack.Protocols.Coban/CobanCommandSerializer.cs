using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Coban;

/// <summary>
/// Serializa comandos salientes al formato wire de Coban TK103B.
/// Formato: **,imei:{IMEI},{LETTER},{HHMMSS}[,{PARAMS}]\r\n
///
/// Los codigos LETRA son los que el servidor envia al dispositivo.
/// Los codigos NUMERICOS (109, 110, 111, 112...) son los que el dispositivo
/// envia de vuelta como ACK — esos van en el parser, no aqui.
/// Referencia: traccar CobanProtocolEncoder, protocolo TK103B estandar.
/// </summary>
public sealed class CobanCommandSerializer : IProtocolCommandSerializer
{
    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Coban;

    // Letra que el servidor envia al dispositivo para cada tipo de comando.
    // B=posicion, C=cortar motor, D=restaurar motor, E=armar, F=desarmar.
    // Los comandos sin letra conocida (alarmas, geocerca) usan codigos numericos
    // del protocolo extendido Coban — pueden no funcionar en todos los firmwares.
    private static readonly Dictionary<DeviceCommandType, string> _keywords = new()
    {
        [DeviceCommandType.Arm]                  = "E",
        [DeviceCommandType.Disarm]               = "F",
        [DeviceCommandType.CutMotor]             = "C",
        [DeviceCommandType.RestoreMotor]         = "D",
        [DeviceCommandType.RequestSinglePosition] = "B",
        [DeviceCommandType.SetMovementAlarm]     = "105",
        [DeviceCommandType.CancelMovementAlarm]  = "106",
        [DeviceCommandType.SetOverspeedAlarm]    = "107",
        [DeviceCommandType.SetGeofence]          = "114",
        [DeviceCommandType.CancelGeofence]       = "115",
        [DeviceCommandType.CancelAlarm]          = "104",
    };

    /// <inheritdoc />
    public bool Supports(DeviceCommandType type) => _keywords.ContainsKey(type);

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize(DeviceCommand command)
    {
        if (!_keywords.TryGetValue(command.Type, out string? keyword))
        {
            throw new CommandNotSupportedByProtocolException(command.Type, ProtocolId.Coban);
        }

        string hhmmss = DateTimeOffset.UtcNow.ToString("HHmmss");

        string wire = command.Type switch
        {
            DeviceCommandType.SetMovementAlarm   => BuildWithRadius(command, keyword, hhmmss),
            DeviceCommandType.SetOverspeedAlarm  => BuildWithSpeed(command, keyword, hhmmss),
            DeviceCommandType.SetGeofence        => BuildWithGeofence(command, keyword, hhmmss),
            _                                    => $"**,imei:{command.Imei},{keyword},{hhmmss}\r\n",
        };

        return Encoding.ASCII.GetBytes(wire);
    }

    private static string BuildWithRadius(DeviceCommand command, string keyword, string hhmmss)
    {
        string? rawRadius = command.Parameters?["radius"]?.ToString();
        if (string.IsNullOrWhiteSpace(rawRadius))
            throw new CommandParameterMissingException("radius");

        if (!int.TryParse(rawRadius, out int radius) || radius <= 0)
            throw new ArgumentException(
                $"El parametro 'radius' debe ser un entero positivo. Valor recibido: '{rawRadius}'.",
                nameof(command));

        return $"**,imei:{command.Imei},{keyword},{hhmmss},{radius.ToString("D5")}\r\n";
    }

    private static string BuildWithSpeed(DeviceCommand command, string keyword, string hhmmss)
    {
        string? rawSpeed = command.Parameters?["speed"]?.ToString();
        if (string.IsNullOrWhiteSpace(rawSpeed))
            throw new CommandParameterMissingException("speed");

        if (!int.TryParse(rawSpeed, out int speed) || speed <= 0)
            throw new ArgumentException(
                $"El parametro 'speed' debe ser un entero positivo. Valor recibido: '{rawSpeed}'.",
                nameof(command));

        return $"**,imei:{command.Imei},{keyword},{hhmmss},{speed.ToString("D3")}\r\n";
    }

    private static string BuildWithGeofence(DeviceCommand command, string keyword, string hhmmss)
    {
        string? latTL = command.Parameters?["latTL"]?.ToString();
        string? lonTL = command.Parameters?["lonTL"]?.ToString();
        string? latBR = command.Parameters?["latBR"]?.ToString();
        string? lonBR = command.Parameters?["lonBR"]?.ToString();

        if (string.IsNullOrWhiteSpace(latTL)) throw new CommandParameterMissingException("latTL");
        if (string.IsNullOrWhiteSpace(lonTL)) throw new CommandParameterMissingException("lonTL");
        if (string.IsNullOrWhiteSpace(latBR)) throw new CommandParameterMissingException("latBR");
        if (string.IsNullOrWhiteSpace(lonBR)) throw new CommandParameterMissingException("lonBR");

        return $"**,imei:{command.Imei},{keyword},{hhmmss},{latTL},{lonTL};{latBR},{lonBR}\r\n";
    }
}
