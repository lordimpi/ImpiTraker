using System.Text.Json.Nodes;

namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Representacion normalizada de un comando saliente hacia un dispositivo GPS.
/// </summary>
/// <param name="CommandId">Identificador unico del comando.</param>
/// <param name="Imei">IMEI del dispositivo destino.</param>
/// <param name="Type">Tipo de comando a ejecutar.</param>
/// <param name="Parameters">Parametros opcionales del comando en formato JSON.</param>
/// <param name="CreatedAtUtc">Marca de tiempo UTC de creacion del comando.</param>
public sealed record DeviceCommand(
    Guid CommandId,
    string Imei,
    DeviceCommandType Type,
    JsonObject? Parameters,
    DateTimeOffset CreatedAtUtc);
