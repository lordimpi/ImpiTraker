using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Coban;

/// <summary>
/// Serializa comandos salientes al formato wire Baanool 403CD.
/// Formato: **,imei:{IMEI},{CODE}[,{PARAMS}]  (sin terminador, sin password).
/// Arm/Disarm/Motor usan GPS103 letter codes (L/M/J/K/B) — el 403CD no responde a los numéricos equivalentes.
/// Alarmas y geocerca usan Coban GPRS numérico (104-115) — ACK simétrico, correlación correcta.
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

    // El 403CD acepta dos dialectos según el comando:
    // - GPS103 letter codes (L/M/J/K/B): Arm/Disarm/CutMotor/RestoreMotor/Position — confirmado en dispositivo.
    //   El dispositivo NO ACKea estos con código numérico: el status quedará Timeout (comportamiento esperado).
    // - Coban GPRS numérico (ref: "coban GPRS protocol.pdf", sección 3.1): alarmas y geocerca — ACK simétrico,
    //   correlación cierra correctamente en el servidor.
    private static readonly Dictionary<DeviceCommandType, string> _keywords = new()
    {
        [DeviceCommandType.Arm]                   = "L",
        [DeviceCommandType.Disarm]                = "M",
        [DeviceCommandType.CutMotor]              = "J",
        [DeviceCommandType.RestoreMotor]          = "K",
        [DeviceCommandType.RequestSinglePosition] = "B",
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
