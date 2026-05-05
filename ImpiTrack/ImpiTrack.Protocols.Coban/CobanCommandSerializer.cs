using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Coban;

/// <summary>
/// Serializa comandos salientes al formato wire Baanool 403CD.
/// Formato: **,imei:{IMEI},{CODE}[,{PARAMS}]  (sin terminador, sin password).
/// Todos los códigos son numéricos Coban GPRS (ref: "coban GPRS protocol.pdf", sección 3.1).
/// El dispositivo ACKea con el mismo código, lo que permite correlación exacta server-side.
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

    // Todos los comandos usan códigos numéricos del protocolo Coban GPRS (ref: "coban GPRS protocol.pdf", sección 3.1).
    // El dispositivo ACKea con el mismo código numérico que recibe, lo que permite correlación exacta en el servidor.
    // Los letter codes GPS103 (L/M/J/K/B) ejecutan en el dispositivo pero no generan ACK numérico —
    // la correlación nunca cierra y el comando queda en Timeout.
    private static readonly Dictionary<DeviceCommandType, string> _keywords = new()
    {
        [DeviceCommandType.Arm]                   = "111",
        [DeviceCommandType.Disarm]                = "112",
        [DeviceCommandType.CutMotor]              = "109",
        [DeviceCommandType.RestoreMotor]          = "110",
        [DeviceCommandType.RequestSinglePosition] = "100",
        [DeviceCommandType.SetMovementAlarm]      = "105",
        [DeviceCommandType.CancelMovementAlarm]   = "106",
        [DeviceCommandType.SetOverspeedAlarm]     = "107",
        [DeviceCommandType.SetGeofence]           = "114",
        [DeviceCommandType.CancelGeofence]        = "115",
        [DeviceCommandType.CancelAlarm]           = "104",
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

    // Formato GPS103 (ref: Traccar Gps103ProtocolEncoder): **,imei:IMEI,KEYWORD
    private string BuildSimple(string imei, string keyword) =>
        $"**,imei:{imei},{keyword}";

    private static string BuildWithRadius(DeviceCommand command, string keyword)
    {
        string? rawRadius = command.Parameters?["radius"]?.ToString();
        if (string.IsNullOrWhiteSpace(rawRadius))
            throw new CommandParameterMissingException("radius");

        if (!int.TryParse(rawRadius, out int radius) || radius <= 0)
            throw new ArgumentException(
                $"El parametro 'radius' debe ser un entero positivo. Valor recibido: '{rawRadius}'.",
                nameof(command));

        return $"**,imei:{command.Imei},{keyword},{radius}";
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

        return $"**,imei:{command.Imei},{keyword},{speed.ToString("D3")}";
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

        return $"**,imei:{command.Imei},{keyword},{latTL},{lonTL};{latBR},{lonBR}";
    }
}
