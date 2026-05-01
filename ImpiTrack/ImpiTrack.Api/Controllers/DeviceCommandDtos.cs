using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Api.Controllers;

/// <summary>
/// Cuerpo de la solicitud para encolar un comando de dispositivo.
/// </summary>
/// <param name="Type">Tipo de comando a ejecutar.</param>
/// <param name="Parameters">Parametros opcionales del comando (radiusMeters, speedKmh, coordenadas de geocerca, etc.).</param>
/// <param name="Confirm">Marca de confirmacion explicita requerida para comandos peligrosos (CutMotor, Reset).</param>
public sealed record EnqueueCommandRequest(
    DeviceCommandType Type,
    Dictionary<string, object>? Parameters,
    bool? Confirm);

/// <summary>
/// Respuesta al encolar un comando de dispositivo exitosamente.
/// </summary>
/// <param name="CommandId">Identificador unico del comando creado.</param>
/// <param name="Status">Estado inicial: <c>Queued</c> si el dispositivo esta offline, <c>Sent</c> si fue despachado de inmediato.</param>
/// <param name="QueuedAtUtc">Marca de tiempo UTC en que el comando fue creado.</param>
public sealed record EnqueueCommandResponse(
    Guid CommandId,
    string Status,
    DateTimeOffset QueuedAtUtc);

/// <summary>
/// Representacion publica de un comando de dispositivo para consumo de la API.
/// </summary>
/// <param name="CommandId">Identificador unico del comando.</param>
/// <param name="Imei">IMEI del dispositivo destino.</param>
/// <param name="Type">Tipo de comando como cadena.</param>
/// <param name="Status">Estado actual del ciclo de vida.</param>
/// <param name="QueuedAtUtc">Marca de tiempo UTC de creacion.</param>
/// <param name="SentAtUtc">Marca de tiempo UTC de envio al dispositivo. Null hasta ese momento.</param>
/// <param name="AckedAtUtc">Marca de tiempo UTC del ACK recibido. Null hasta ese momento.</param>
/// <param name="ResponseCode">Codigo de respuesta del dispositivo. Null hasta recibir el ACK.</param>
/// <param name="ResponseText">Texto de respuesta completo del dispositivo. Null hasta recibir el ACK.</param>
/// <param name="FailureReason">Razon del fallo cuando el estado es terminal negativo. Null en casos exitosos.</param>
public sealed record DeviceCommandDto(
    Guid CommandId,
    string Imei,
    string Type,
    string Status,
    DateTimeOffset QueuedAtUtc,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? AckedAtUtc,
    string? ResponseCode,
    string? ResponseText,
    string? FailureReason);
