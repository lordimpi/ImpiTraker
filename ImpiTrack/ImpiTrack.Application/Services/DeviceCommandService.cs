using System.Text.Json;
using System.Text.Json.Nodes;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace ImpiTrack.Application.Services;

/// <summary>
/// Implementacion del servicio de aplicacion para el ciclo de vida de comandos de dispositivo.
/// Orquesta validaciones, persistencia, despacho al canal de salida y procesamiento de ACKs.
/// </summary>
public sealed class DeviceCommandService : IDeviceCommandService
{
    private static readonly HashSet<DeviceCommandType> DangerousCommands = new()
    {
        DeviceCommandType.CutMotor,
        DeviceCommandType.Reset
    };

    /// <summary>
    /// Codigos Coban que indican fallo del comando. Mapean a status=Failed con failure_reason.
    /// 511 = arm rechazado; 525/526 = remote start fallo; 510/512 = ejecucion no posible.
    /// </summary>
    private static readonly Dictionary<string, string> CobanFailureCodes = new(StringComparer.Ordinal)
    {
        ["511"] = "arm_failed",
        ["525"] = "remote_start_failed",
        ["526"] = "remote_start_unavailable",
        ["510"] = "engine_cut_failed",
        ["512"] = "engine_restore_failed"
    };

    /// <summary>
    /// Codigo Coban que indica ACK con resultado diferido (motor cut postpuesto a velocidad &lt;20).
    /// El comando se considera Acknowledged con texto descriptivo.
    /// </summary>
    private const string CobanDeferredCutMotor = "509";

    private readonly IDeviceCommandRepository _repo;
    private readonly IDeviceCommandValidator _validator;
    private readonly IDeviceCommandOwnershipGuard _ownershipGuard;
    private readonly IDeviceProtocolResolver _protocolResolver;
    private readonly IDeviceCommandNotifier _notifier;
    private readonly ISessionManager _sessionManager;
    private readonly IReadOnlyDictionary<ProtocolId, IProtocolCommandSerializer> _serializers;
    private readonly ILogger<DeviceCommandService> _logger;

    /// <summary>
    /// Crea una nueva instancia del servicio.
    /// </summary>
    public DeviceCommandService(
        IDeviceCommandRepository repo,
        IDeviceCommandValidator validator,
        IDeviceCommandOwnershipGuard ownershipGuard,
        IDeviceProtocolResolver protocolResolver,
        IDeviceCommandNotifier notifier,
        ISessionManager sessionManager,
        IEnumerable<IProtocolCommandSerializer> serializers,
        ILogger<DeviceCommandService> logger)
    {
        _repo = repo;
        _validator = validator;
        _ownershipGuard = ownershipGuard;
        _protocolResolver = protocolResolver;
        _notifier = notifier;
        _sessionManager = sessionManager;
        _logger = logger;
        _serializers = serializers.ToDictionary(s => s.Protocol);
    }

    /// <inheritdoc />
    public async Task<EnqueueResult> EnqueueAsync(
        string imei,
        Guid userId,
        DeviceCommandType type,
        IDictionary<string, object>? parameters,
        bool confirm,
        string userIp,
        string userAgent,
        CancellationToken ct)
    {
        // 1. Ownership.
        if (!await _ownershipGuard.IsOwnerAsync(imei, userId, ct))
        {
            _logger.LogWarning(
                "device_command_rejected reason=not_found imei={Imei} userId={UserId} type={Type}",
                imei, userId, type);
            return new EnqueueResult(false, null, "not_found", "El IMEI no esta vinculado al usuario.");
        }

        // 2. Comandos peligrosos requieren confirmacion explicita.
        if (DangerousCommands.Contains(type) && !confirm)
        {
            return new EnqueueResult(
                false, null, "confirmation_required",
                "Este comando requiere confirmacion explicita (campo 'confirm' = true).");
        }

        // 3. Resolver protocolo.
        ProtocolId? resolved = await _protocolResolver.ResolveForDeviceAsync(imei, ct);
        if (resolved is null || resolved == ProtocolId.Unknown)
        {
            _logger.LogWarning(
                "device_command_rejected reason=protocol_unknown imei={Imei} type={Type}",
                imei, type);
            return new EnqueueResult(
                false, null, "protocol_unknown",
                "No fue posible resolver el protocolo del dispositivo.");
        }

        ProtocolId protocol = resolved.Value;

        // 4. Localizar serializer.
        if (!_serializers.TryGetValue(protocol, out IProtocolCommandSerializer? serializer))
        {
            _logger.LogWarning(
                "device_command_rejected reason=serializer_missing imei={Imei} protocol={Protocol} type={Type}",
                imei, protocol, type);
            return new EnqueueResult(
                false, null, "protocol_unknown",
                $"Sin serializer registrado para el protocolo '{protocol}'.");
        }

        // 5. Capability check del protocolo.
        if (!serializer.Supports(type))
        {
            return new EnqueueResult(
                false, null, "command_not_supported_by_protocol",
                $"El protocolo '{protocol}' no soporta el comando '{type}'.");
        }

        // 6. Validar parametros.
        DeviceCommandValidationResult validation = _validator.Validate(type, parameters);
        if (!validation.IsValid)
        {
            return new EnqueueResult(false, null, "invalid_parameters", validation.ErrorMessage);
        }

        // 7. Construir comando + serializar.
        Guid commandId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        JsonObject? jsonParameters = ToJsonObject(parameters);
        DeviceCommand command = new(commandId, imei, type, jsonParameters, now);

        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = serializer.Serialize(command);
        }
        catch (CommandNotSupportedByProtocolException ex)
        {
            _logger.LogWarning(ex,
                "device_command_serialize_unsupported imei={Imei} protocol={Protocol} type={Type}",
                imei, protocol, type);
            return new EnqueueResult(false, null, "command_not_supported_by_protocol", ex.Message);
        }
        catch (CommandParameterMissingException ex)
        {
            _logger.LogWarning(ex,
                "device_command_serialize_param_missing imei={Imei} protocol={Protocol} type={Type} field={Field}",
                imei, protocol, type, ex.FieldName);
            return new EnqueueResult(false, null, "invalid_parameters", ex.Message);
        }

        string payloadText = System.Text.Encoding.ASCII.GetString(bytes.Span);
        (string? correlationKey, string? correlationTimestamp) = ExtractCorrelation(protocol, payloadText, type);

        DeviceCommandRecord record = new()
        {
            CommandId = commandId,
            Imei = imei,
            UserId = userId,
            CommandType = type.ToString(),
            Parameters = jsonParameters?.ToJsonString(),
            Protocol = protocol.ToString(),
            PayloadSent = null, // se rellena al transicionar a Sent
            CorrelationKey = correlationKey,
            CorrelationTimestamp = correlationTimestamp,
            Status = "Queued",
            QueuedAtUtc = now,
            UserIp = userIp,
            UserAgent = userAgent
        };

        // 8. Persistir como Queued ANTES de encolar (regla AD-5: persistencia primero).
        // payload_sent / correlation_* permanecen NULL en este punto: el write-loop los rellena
        // tras el stream.WriteAsync exitoso via MarkSentAsync (REQ-LC-3, REQ-AUDIT-1).
        // Nota: las correlation keys precomputadas (`correlationKey`, `correlationTimestamp`)
        // se usan al hacer MarkSentAsync; aqui no se persisten para evitar
        // discrepancia con los bytes realmente despachados.
        _ = correlationKey; _ = correlationTimestamp; _ = payloadText;
        await _repo.InsertAsync(record, ct);

        // 9. Intentar despachar al canal de salida. La transicion a Sent (sent_at_utc, payload_sent,
        //    correlation_key, correlation_timestamp) la realiza el write-loop al confirmar el envio.
        bool dispatched = _sessionManager.TryEnqueueCommand(imei, new OutboundFrame.Command(command));
        if (!dispatched)
        {
            _logger.LogInformation(
                "device_command_queued_offline imei={Imei} commandId={CommandId} type={Type}",
                imei, commandId, type);
        }

        // 10. Notificacion best-effort en estado Queued (la transicion Sent emite su propia notificacion).
        await SafeNotifyAsync(userId, record, ct);

        return new EnqueueResult(true, commandId, ErrorCode: dispatched ? null : "device_offline", ErrorMessage: null);
    }

    /// <inheritdoc />
    public async Task MarkSentAsync(
        Guid commandId,
        string payloadSent,
        string? correlationKey,
        string? correlationTimestamp,
        CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool transitioned = await _repo.UpdateStatusAsync(
            commandId,
            "Sent",
            now,
            payloadSent,
            correlationKey,
            correlationTimestamp,
            responseCode: null,
            responseText: null,
            failureReason: null,
            ct);

        if (!transitioned)
        {
            _logger.LogInformation(
                "device_command_mark_sent_skipped commandId={CommandId} reason=already_terminal_or_missing",
                commandId);
            return;
        }

        DeviceCommandRecord? record = await _repo.GetByIdAsync(commandId, ct);
        if (record is null)
        {
            // Defensive: a row updated by UpdateStatusAsync should still exist; only log.
            _logger.LogWarning(
                "device_command_mark_sent_record_missing commandId={CommandId}",
                commandId);
            return;
        }

        await SafeNotifyAsync(record.UserId, record, ct);
    }

    /// <inheritdoc />
    public async Task<DeviceCommandRecord?> GetByIdAsync(Guid commandId, Guid userId, CancellationToken ct)
    {
        DeviceCommandRecord? record = await _repo.GetByIdAsync(commandId, ct);
        if (record is null) return null;
        if (record.UserId != userId) return null;
        return record;
    }

    /// <inheritdoc />
    public Task<PagedResult<DeviceCommandRecord>> ListAsync(
        string imei,
        Guid userId,
        DeviceCommandListQuery query,
        CancellationToken ct)
    {
        return _repo.ListPagedAsync(imei, userId, query, ct);
    }

    /// <inheritdoc />
    public async Task HandleAckAsync(
        string imei,
        ProtocolId protocol,
        string responseCode,
        string? correlationKey,
        string? correlationTimestamp,
        CancellationToken ct)
    {
        DeviceCommandRecord? target = protocol switch
        {
            ProtocolId.Coban => await ResolveCobanTargetAsync(imei, responseCode, correlationKey, ct),
            ProtocolId.Cantrack => await ResolveCantrackTargetAsync(imei, correlationKey, correlationTimestamp, ct),
            _ => null
        };

        if (target is null)
        {
            _logger.LogWarning(
                "device_command_ack_unmatched imei={Imei} protocol={Protocol} responseCode={ResponseCode} correlationKey={CorrelationKey} correlationTimestamp={CorrelationTimestamp}",
                imei, protocol, responseCode, correlationKey, correlationTimestamp);
            return;
        }

        (string finalStatus, string? failureReason, string? responseText) = ResolveAckTransition(protocol, responseCode);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool updated = await _repo.UpdateStatusAsync(
            target.CommandId,
            finalStatus,
            now,
            payloadSent: null,
            correlationKey: null,
            correlationTimestamp: null,
            responseCode: responseCode,
            responseText: responseText,
            failureReason: failureReason,
            ct);

        if (!updated)
        {
            _logger.LogInformation(
                "device_command_ack_skipped commandId={CommandId} reason=already_terminal_or_missing",
                target.CommandId);
            return;
        }

        target.Status = finalStatus;
        target.AckedAtUtc = now;
        target.ResponseCode = responseCode;
        target.ResponseText = responseText;
        target.FailureReason = failureReason;

        await SafeNotifyAsync(target.UserId, target, ct);
    }

    private async Task<DeviceCommandRecord?> ResolveCobanTargetAsync(
        string imei,
        string responseCode,
        string? correlationKey,
        CancellationToken ct)
    {
        // Coban responde con su keyword (ej: 109/110/111/112) o codigo derivado (509, 511).
        // Mapear codigos derivados a su keyword raiz para el lookup FIFO.
        string keyword = correlationKey ?? MapCobanResponseToKeyword(responseCode);
        if (string.IsNullOrEmpty(keyword))
        {
            return null;
        }

        return await _repo.MatchPendingByKeywordAsync(imei, keyword, ct);
    }

    private async Task<DeviceCommandRecord?> ResolveCantrackTargetAsync(
        string imei,
        string? correlationKey,
        string? correlationTimestamp,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(correlationKey))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(correlationTimestamp))
        {
            DeviceCommandRecord? exact = await _repo.MatchPendingByCorrelationAsync(
                imei, correlationKey, correlationTimestamp, ct);
            if (exact is not null)
            {
                return exact;
            }
        }

        // Fallback FIFO por keyword cuando no hay timestamp (respuestas plaintext "stop engine succeed", etc.).
        return await _repo.MatchPendingByKeywordAsync(imei, correlationKey, ct);
    }

    /// <summary>
    /// Determina el estado final, razon de fallo y texto descriptivo a partir del codigo de respuesta.
    /// </summary>
    private static (string Status, string? FailureReason, string? ResponseText) ResolveAckTransition(
        ProtocolId protocol,
        string responseCode)
    {
        if (protocol == ProtocolId.Coban)
        {
            if (CobanFailureCodes.TryGetValue(responseCode, out string? reason))
            {
                return ("Failed", reason, $"coban_response_code={responseCode}");
            }

            if (responseCode == CobanDeferredCutMotor)
            {
                return ("Acknowledged", null, "motor cut deferred until speed<20");
            }

            return ("Acknowledged", null, $"coban_response_code={responseCode}");
        }

        if (protocol == ProtocolId.Cantrack)
        {
            // Cantrack: V4 echo (CMD) y plaintext "stop engine succeed" se consideran exitos.
            // Tipo de fallo conocido: "fail" en plaintext (pendiente para v2 — TD-6).
            return ("Acknowledged", null, $"cantrack_response_code={responseCode}");
        }

        return ("Acknowledged", null, responseCode);
    }

    /// <summary>
    /// Mapea codigos derivados de Coban a su keyword de comando para FIFO matching.
    /// 509 -&gt; 109 (motor cut deferred), 511 -&gt; 111 (arm failed), 525/526 -&gt; 113 (remote start),
    /// 510 -&gt; 109 (engine cut), 512 -&gt; 110 (engine restore).
    /// Cualquier otro codigo se asume keyword directo (ej: 100/102/104/...).
    /// </summary>
    private static string MapCobanResponseToKeyword(string responseCode) => responseCode switch
    {
        "509" or "510" => "109",
        "511" => "111",
        "512" => "110",
        "525" or "526" => "113",
        _ => responseCode
    };

    /// <summary>
    /// Extrae claves de correlacion del payload serializado.
    /// Coban: keyword (3 digitos) tras el segundo separador (",imei:NNN,KW...").
    /// Cantrack: CMD del frame V4 (parts[2]) y hhmmss (parts[3]).
    /// </summary>
    private static (string? CorrelationKey, string? CorrelationTimestamp) ExtractCorrelation(
        ProtocolId protocol,
        string payloadText,
        DeviceCommandType type)
    {
        if (string.IsNullOrEmpty(payloadText))
        {
            return (null, null);
        }

        if (protocol == ProtocolId.Coban)
        {
            // Format: "**,imei:NNN,KEYWORD[,extra]\r\n"
            string[] parts = payloadText.Split(',');
            if (parts.Length >= 3)
            {
                string keyword = parts[2].Split(',', '\r', '\n')[0].Trim();
                return (keyword, null);
            }

            return (null, null);
        }

        if (protocol == ProtocolId.Cantrack)
        {
            // Format: "*HQ,IMEI,CMD,hhmmss,body#"
            string trimmed = payloadText.TrimStart('*').TrimEnd('#').Trim('\r', '\n');
            string[] parts = trimmed.Split(',');
            if (parts.Length >= 4)
            {
                string cmd = parts[2].Trim();
                string ts = parts[3].Trim();
                return (cmd, ts);
            }

            return (null, null);
        }

        // Otros protocolos: dejar correlacion en blanco. El caller documentara la limitacion.
        _ = type;
        return (null, null);
    }

    private static JsonObject? ToJsonObject(IDictionary<string, object>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        JsonObject obj = new();
        foreach (KeyValuePair<string, object> kv in parameters)
        {
            obj[kv.Key] = kv.Value switch
            {
                null => null,
                JsonNode node => node.DeepClone(),
                string s => JsonValue.Create(s),
                bool b => JsonValue.Create(b),
                int i => JsonValue.Create(i),
                long l => JsonValue.Create(l),
                double d => JsonValue.Create(d),
                float f => JsonValue.Create(f),
                decimal dec => JsonValue.Create(dec),
                _ => JsonValue.Create(JsonSerializer.SerializeToElement(kv.Value))
            };
        }

        return obj;
    }

    private async Task SafeNotifyAsync(Guid userId, DeviceCommandRecord record, CancellationToken ct)
    {
        try
        {
            await _notifier.NotifyStatusChangedAsync(userId, record, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "device_command_notify_error commandId={CommandId} userId={UserId} status={Status}",
                record.CommandId, userId, record.Status);
        }
    }
}
