using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Coban;

/// <summary>
/// Serializa comandos salientes al formato wire Baanool 403CD.
/// Formato: **,imei:{IMEI},{LETTER}[,{PASSWORD}];
///
/// Los codigos LETRA son los que el servidor envia al dispositivo (A/B/G/J/K).
/// Los codigos NUMERICOS (109, 110, 111, 112...) son los que el dispositivo
/// envia de vuelta como ACK — esos van en el parser, no aqui.
/// Terminador: punto y coma (;). Password incluido si password != "".
/// Referencia: protocolo Baanool/Coban serie 403.
/// </summary>
public sealed class CobanCommandSerializer : IProtocolCommandSerializer
{
    private readonly string _password;

    /// <summary>
    /// Crea el serializer con password opcional para el formato de comandos.
    /// </summary>
    /// <param name="password">Contraseña del dispositivo. Vacío = sin contraseña en el frame.</param>
    public CobanCommandSerializer(string password = "")
    {
        _password = password ?? string.Empty;
    }

    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Coban;

    // Letra que el servidor envia al dispositivo para cada tipo de comando.
    // B=posicion, C=cortar motor, D=restaurar motor, E=armar, F=desarmar.
    // Los comandos sin letra conocida (alarmas, geocerca) usan codigos numericos
    // del protocolo extendido Coban — pueden no funcionar en todos los firmwares.
    private static readonly Dictionary<DeviceCommandType, string> _keywords = new()
    {
        [DeviceCommandType.Arm]                  = "A",
        [DeviceCommandType.Disarm]               = "B",
        [DeviceCommandType.CutMotor]             = "J",
        [DeviceCommandType.RestoreMotor]         = "K",
        [DeviceCommandType.RequestSinglePosition] = "G",
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

        string wire = command.Type switch
        {
            DeviceCommandType.SetMovementAlarm   => BuildWithRadius(command, keyword),
            DeviceCommandType.SetOverspeedAlarm  => BuildWithSpeed(command, keyword),
            DeviceCommandType.SetGeofence        => BuildWithGeofence(command, keyword),
            _                                    => BuildSimple(command.Imei, keyword),
        };

        return Encoding.ASCII.GetBytes(wire);
    }

    private string BuildSimple(string imei, string keyword) =>
        string.IsNullOrEmpty(_password)
            ? $"##,imei:{imei},{keyword};"
            : $"##,imei:{imei},{keyword},{_password};";

    private static string BuildWithRadius(DeviceCommand command, string keyword)
    {
        string? rawRadius = command.Parameters?["radius"]?.ToString();
        if (string.IsNullOrWhiteSpace(rawRadius))
            throw new CommandParameterMissingException("radius");

        if (!int.TryParse(rawRadius, out int radius) || radius <= 0)
            throw new ArgumentException(
                $"El parametro 'radius' debe ser un entero positivo. Valor recibido: '{rawRadius}'.",
                nameof(command));

        return $"**,imei:{command.Imei},{keyword},{radius.ToString("D5")};";
    }

    private static string BuildWithSpeed(DeviceCommand command, string keyword)
    {
        string? rawSpeed = command.Parameters?["speed"]?.ToString();
        if (string.IsNullOrWhiteSpace(rawSpeed))
            throw new CommandParameterMissingException("speed");

        if (!int.TryParse(rawSpeed, out int speed) || speed <= 0)
            throw new ArgumentException(
                $"El parametro 'speed' debe ser un entero positivo. Valor recibido: '{rawSpeed}'.",
                nameof(command));

        return $"**,imei:{command.Imei},{keyword},{speed.ToString("D3")};";
    }

    private static string BuildWithGeofence(DeviceCommand command, string keyword)
    {
        string? latTL = command.Parameters?["latTL"]?.ToString();
        string? lonTL = command.Parameters?["lonTL"]?.ToString();
        string? latBR = command.Parameters?["latBR"]?.ToString();
        string? lonBR = command.Parameters?["lonBR"]?.ToString();

        if (string.IsNullOrWhiteSpace(latTL)) throw new CommandParameterMissingException("latTL");
        if (string.IsNullOrWhiteSpace(lonTL)) throw new CommandParameterMissingException("lonTL");
        if (string.IsNullOrWhiteSpace(latBR)) throw new CommandParameterMissingException("latBR");
        if (string.IsNullOrWhiteSpace(lonBR)) throw new CommandParameterMissingException("lonBR");

        return $"**,imei:{command.Imei},{keyword},{latTL},{lonTL};{latBR},{lonBR};";
    }
}
