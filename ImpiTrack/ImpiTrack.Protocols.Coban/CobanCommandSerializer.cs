using System.Text;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Protocols.Coban;

/// <summary>
/// Serializa comandos salientes al formato wire de Coban.
/// Formato base: **,imei:{IMEI},{KEYWORD}[,{PARAMS}]\r\n
/// </summary>
public sealed class CobanCommandSerializer : IProtocolCommandSerializer
{
    /// <inheritdoc />
    public ProtocolId Protocol => ProtocolId.Coban;

    // Mapa de comandos soportados a sus keywords de wire.
    // Reset no esta incluido — Coban no soporta reset via protocolo.
    private static readonly Dictionary<DeviceCommandType, string> _keywords = new()
    {
        [DeviceCommandType.Arm]                  = "111",
        [DeviceCommandType.Disarm]               = "112",
        [DeviceCommandType.CutMotor]             = "109",
        [DeviceCommandType.RestoreMotor]         = "110",
        [DeviceCommandType.SetMovementAlarm]     = "105",
        [DeviceCommandType.CancelMovementAlarm]  = "106",
        [DeviceCommandType.SetOverspeedAlarm]    = "107",
        [DeviceCommandType.SetGeofence]          = "114",
        [DeviceCommandType.CancelGeofence]       = "115",
        [DeviceCommandType.CancelAlarm]          = "104",
        [DeviceCommandType.RequestSinglePosition] = "100",
    };

    /// <inheritdoc />
    public bool Supports(DeviceCommandType type) => _keywords.ContainsKey(type);

    /// <inheritdoc />
    /// <exception cref="CommandNotSupportedByProtocolException">
    /// El tipo de comando no esta soportado por Coban (ej: Reset).
    /// </exception>
    /// <exception cref="CommandParameterMissingException">
    /// Falta un parametro requerido (radius, speed, latTL, lonTL, latBR, lonBR).
    /// </exception>
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
            _                                    => Build(command.Imei, keyword),
        };

        return Encoding.ASCII.GetBytes(wire);
    }

    // -------------------------------------------------------------------------
    // Builders
    // -------------------------------------------------------------------------

    private static string Build(string imei, string keyword)
        => $"**,imei:{imei},{keyword}\r\n";

    private static string BuildWithRadius(DeviceCommand command, string keyword)
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

        // Coban espera el radio como string de 5 digitos con ceros a la izquierda.
        string paddedRadius = radius.ToString("D5");
        return $"**,imei:{command.Imei},{keyword},{paddedRadius}\r\n";
    }

    private static string BuildWithSpeed(DeviceCommand command, string keyword)
    {
        string? rawSpeed = command.Parameters?["speed"]?.ToString();
        if (string.IsNullOrWhiteSpace(rawSpeed))
        {
            throw new CommandParameterMissingException("speed");
        }

        if (!int.TryParse(rawSpeed, out int speed) || speed <= 0)
        {
            throw new ArgumentException(
                $"El parametro 'speed' debe ser un entero positivo. Valor recibido: '{rawSpeed}'.",
                nameof(command));
        }

        // Coban espera la velocidad como string de 3 digitos con ceros a la izquierda.
        string paddedSpeed = speed.ToString("D3");
        return $"**,imei:{command.Imei},{keyword},{paddedSpeed}\r\n";
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

        // Formato Coban geocerca: **,imei:IMEI,114,latTL,lonTL;latBR,lonBR
        return $"**,imei:{command.Imei},{keyword},{latTL},{lonTL};{latBR},{lonBR}\r\n";
    }
}
