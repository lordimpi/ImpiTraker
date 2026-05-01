using Dapper;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;
using ImpiTrack.Protocols.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ImpiTrack.DataAccess.Repositories;

/// <summary>
/// Resuelve el protocolo de un IMEI consultando primero <c>user_devices.protocol</c>
/// (string del enum <see cref="ProtocolId"/>) y como fallback la posicion mas reciente
/// (<c>positions.protocol</c> almacenado como entero).
/// Cachea cada resolucion durante 60 segundos para evitar consultas repetitivas.
/// </summary>
public sealed class DeviceProtocolResolver : IDeviceProtocolResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DatabaseRuntimeContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<DeviceProtocolResolver> _logger;

    /// <summary>Crea un nuevo resolver.</summary>
    public DeviceProtocolResolver(
        IDbConnectionFactory connectionFactory,
        DatabaseRuntimeContext context,
        IMemoryCache cache,
        ILogger<DeviceProtocolResolver> logger)
    {
        _connectionFactory = connectionFactory;
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProtocolId?> ResolveForDeviceAsync(string imei, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imei)) return null;

        string cacheKey = $"protocol:{imei}";
        if (_cache.TryGetValue(cacheKey, out ProtocolId? cached) && cached.HasValue)
        {
            return cached;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        // 1. user_devices.protocol (string, ej: "Coban", "Cantrack").
        const string sqlUserDevices = """
            SELECT protocol
            FROM user_devices
            WHERE imei = @Imei
              AND is_active = TRUE
              AND protocol IS NOT NULL
            """;

        // SQL Server usa BIT 1 en lugar de TRUE.
        string normalizedSqlUserDevices = _context.Provider == DatabaseProvider.SqlServer
            ? sqlUserDevices.Replace("is_active = TRUE", "is_active = 1", StringComparison.Ordinal)
            : sqlUserDevices;

        CommandDefinition userDeviceCmd = new(
            normalizedSqlUserDevices,
            new { Imei = imei },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        string? protocolText = await connection.QueryFirstOrDefaultAsync<string?>(userDeviceCmd);
        if (TryParseProtocol(protocolText, out ProtocolId resolved))
        {
            _cache.Set(cacheKey, resolved, CacheTtl);
            return resolved;
        }

        // 2. Fallback: ultima posicion conocida (positions.protocol es INTEGER).
        string limitClause = _context.Provider == DatabaseProvider.Postgres
            ? "LIMIT 1"
            : "FETCH FIRST 1 ROWS ONLY";

        string sqlPositions = $"""
            SELECT protocol
            FROM positions
            WHERE imei = @Imei
            ORDER BY gps_time_utc DESC
            {limitClause}
            """;

        CommandDefinition positionCmd = new(
            sqlPositions,
            new { Imei = imei },
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        int? protocolNumeric = await connection.QueryFirstOrDefaultAsync<int?>(positionCmd);
        if (protocolNumeric.HasValue && Enum.IsDefined(typeof(ProtocolId), protocolNumeric.Value))
        {
            ProtocolId fromPositions = (ProtocolId)protocolNumeric.Value;
            if (fromPositions != ProtocolId.Unknown)
            {
                _cache.Set(cacheKey, fromPositions, CacheTtl);
                return fromPositions;
            }
        }

        _logger.LogDebug(
            "protocol_resolver_unresolved imei={Imei} userDeviceText={ProtocolText} positionNumeric={ProtocolNumeric}",
            imei, protocolText, protocolNumeric);

        return null;
    }

    /// <summary>
    /// Parsea el texto en <c>user_devices.protocol</c> al enum <see cref="ProtocolId"/>.
    /// Acepta tanto el nombre del enum (case-insensitive) como su valor numerico textual.
    /// </summary>
    private static bool TryParseProtocol(string? text, out ProtocolId resolved)
    {
        resolved = ProtocolId.Unknown;
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (Enum.TryParse(text, ignoreCase: true, out ProtocolId byName) && byName != ProtocolId.Unknown)
        {
            resolved = byName;
            return true;
        }

        if (int.TryParse(text, out int byInt) && Enum.IsDefined(typeof(ProtocolId), byInt))
        {
            ProtocolId mapped = (ProtocolId)byInt;
            if (mapped != ProtocolId.Unknown)
            {
                resolved = mapped;
                return true;
            }
        }

        return false;
    }
}
