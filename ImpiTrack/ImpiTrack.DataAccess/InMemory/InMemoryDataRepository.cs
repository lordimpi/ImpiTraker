using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.Ops;
using ImpiTrack.Protocols.Abstractions;
using ImpiTrack.Tcp.Core.Queue;
using System.Collections.Concurrent;

namespace ImpiTrack.DataAccess.InMemory;

/// <summary>
/// Repositorio en memoria para pruebas locales cuando no hay base de datos SQL configurada.
/// </summary>
public sealed class InMemoryDataRepository : IOpsRepository, IIngestionRepository, IUserAccountRepository
{
    private const string DefaultPlanCode = "BASIC";
    private readonly ConcurrentDictionary<Guid, InMemoryUserAccount> _accounts = new();
    private readonly ConcurrentDictionary<string, Guid> _activeImeiOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private readonly IOpsDataStore _store;

    /// <summary>
    /// Crea un repositorio en memoria con almacenamiento operativo compartido.
    /// </summary>
    /// <param name="store">Almacen operativo en memoria.</param>
    public InMemoryDataRepository(IOpsDataStore store)
    {
        _store = store;
    }

    /// <inheritdoc />
    public Task AddRawPacketAsync(RawPacketRecord record, long backlog, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.AddRawPacket(record, backlog);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertSessionAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.UpsertSession(session);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PersistEnvelopeResult> PersistEnvelopeAsync(InboundEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = envelope;
        return Task.FromResult(new PersistEnvelopeResult(PersistEnvelopeStatus.Persisted));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RawPacketRecord>> GetLatestRawPacketsAsync(string? imei, int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetLatestRawPackets(imei, limit));
    }

    /// <inheritdoc />
    public Task<RawPacketRecord?> GetRawPacketByIdAsync(PacketId packetId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store.TryGetRawPacket(packetId, out RawPacketRecord? record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ErrorAggregate>> GetTopErrorsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string groupBy,
        int limit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetTopErrors(fromUtc, toUtc, groupBy, limit));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionRecord>> GetActiveSessionsAsync(int? port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetActiveSessions(port));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PortIngestionSnapshot>> GetPortSnapshotsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.GetPortSnapshots());
    }

    /// <inheritdoc />
    public Task EnsureUserProvisioningAsync(
        Guid userId,
        string email,
        string? fullName,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _accounts.AddOrUpdate(
            userId,
            _ => new InMemoryUserAccount
            {
                UserId = userId,
                Email = email,
                FullName = fullName,
                PlanCode = DefaultPlanCode,
                PlanName = "Basic",
                MaxGps = 3,
                CreatedAtUtc = nowUtc
            },
            (_, existing) =>
            {
                existing.Email = email;
                existing.FullName = fullName ?? existing.FullName;
                return existing;
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<UserAccountSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult<UserAccountSummary?>(null);
        }

        var summary = new UserAccountSummary(
            account.UserId,
            account.Email,
            account.FullName,
            account.PlanCode,
            account.PlanName,
            account.MaxGps,
            account.Devices.Count);

        return Task.FromResult<UserAccountSummary?>(summary);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserDeviceBinding>> GetUserDevicesAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult<IReadOnlyList<UserDeviceBinding>>([]);
        }

        IReadOnlyList<UserDeviceBinding> devices = account.Devices
            .OrderBy(x => x.Imei, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult(devices);
    }

    /// <inheritdoc />
    public Task<BindDeviceResult> BindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(new BindDeviceResult(BindDeviceStatus.MissingActivePlan, null));
        }

        string normalizedImei = imei.Trim();
        if (normalizedImei.Length == 0)
        {
            return Task.FromResult(new BindDeviceResult(BindDeviceStatus.OwnedByAnotherUser, null));
        }

        lock (_sync)
        {
            UserDeviceBinding? existing = account.Devices
                .FirstOrDefault(x => string.Equals(x.Imei, normalizedImei, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return Task.FromResult(new BindDeviceResult(BindDeviceStatus.AlreadyBound, existing.DeviceId));
            }

            if (_activeImeiOwners.TryGetValue(normalizedImei, out Guid ownerId) && ownerId != userId)
            {
                return Task.FromResult(new BindDeviceResult(BindDeviceStatus.OwnedByAnotherUser, null));
            }

            if (account.Devices.Count >= account.MaxGps)
            {
                return Task.FromResult(new BindDeviceResult(BindDeviceStatus.QuotaExceeded, null));
            }

            Guid deviceId = Guid.NewGuid();
            account.Devices.Add(new UserDeviceBinding(deviceId, normalizedImei, nowUtc));
            _activeImeiOwners[normalizedImei] = userId;
            return Task.FromResult(new BindDeviceResult(BindDeviceStatus.Bound, deviceId));
        }
    }

    /// <inheritdoc />
    public Task<bool> UnbindDeviceAsync(
        Guid userId,
        string imei,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = nowUtc;

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(false);
        }

        lock (_sync)
        {
            UserDeviceBinding? existing = account.Devices
                .FirstOrDefault(x => string.Equals(x.Imei, imei, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                return Task.FromResult(false);
            }

            account.Devices.Remove(existing);
            _activeImeiOwners.TryRemove(existing.Imei, out _);
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserAccountOverview>> GetUsersAsync(int limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int normalizedLimit = Math.Clamp(limit, 1, 500);

        IReadOnlyList<UserAccountOverview> rows = _accounts.Values
            .OrderBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
            .Take(normalizedLimit)
            .Select(x => new UserAccountOverview(
                x.UserId,
                x.Email,
                x.FullName,
                x.PlanCode,
                x.MaxGps,
                x.Devices.Count))
            .ToArray();

        return Task.FromResult(rows);
    }

    /// <inheritdoc />
    public Task<bool> SetUserPlanAsync(
        Guid userId,
        string planCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = nowUtc;

        if (!_accounts.TryGetValue(userId, out InMemoryUserAccount? account))
        {
            return Task.FromResult(false);
        }

        if (!TryResolvePlan(planCode, out string normalizedCode, out string name, out int maxGps))
        {
            return Task.FromResult(false);
        }

        account.PlanCode = normalizedCode;
        account.PlanName = name;
        account.MaxGps = maxGps;
        return Task.FromResult(true);
    }

    private static bool TryResolvePlan(string planCode, out string normalizedCode, out string name, out int maxGps)
    {
        normalizedCode = planCode.Trim().ToUpperInvariant();
        switch (normalizedCode)
        {
            case "BASIC":
                name = "Basic";
                maxGps = 3;
                return true;
            case "PRO":
                name = "Pro";
                maxGps = 10;
                return true;
            case "ENTERPRISE":
                name = "Enterprise";
                maxGps = 100;
                return true;
            default:
                name = string.Empty;
                maxGps = 0;
                return false;
        }
    }

    private sealed class InMemoryUserAccount
    {
        public Guid UserId { get; set; }

        public string Email { get; set; } = string.Empty;

        public string? FullName { get; set; }

        public string PlanCode { get; set; } = "BASIC";

        public string PlanName { get; set; } = "Basic";

        public int MaxGps { get; set; } = 3;

        public DateTimeOffset CreatedAtUtc { get; set; }

        public List<UserDeviceBinding> Devices { get; } = [];
    }
}
