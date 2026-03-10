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
    private static readonly IReadOnlyList<AdminPlanDto> Plans =
    [
        new AdminPlanDto(Guid.Parse("10000000-0000-0000-0000-000000000001"), "BASIC", "Basic", 3, true),
        new AdminPlanDto(Guid.Parse("10000000-0000-0000-0000-000000000002"), "PRO", "Pro", 10, true),
        new AdminPlanDto(Guid.Parse("10000000-0000-0000-0000-000000000003"), "ENTERPRISE", "Enterprise", 100, true)
    ];

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
            _ => CreateAccount(userId, email, fullName, nowUtc),
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
    public Task<PagedResult<UserAccountOverview>> GetUsersAsync(AdminUserListQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, 200);
        string? search = query.Search?.Trim();
        string? planCode = string.IsNullOrWhiteSpace(query.PlanCode)
            ? null
            : query.PlanCode.Trim().ToUpperInvariant();
        string sortBy = query.SortBy.Trim();
        bool descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        IEnumerable<InMemoryUserAccount> filtered = _accounts.Values;

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(x =>
                x.Email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.FullName) &&
                 x.FullName.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (planCode is not null)
        {
            filtered = filtered.Where(x => string.Equals(x.PlanCode, planCode, StringComparison.OrdinalIgnoreCase));
        }

        IEnumerable<InMemoryUserAccount> ordered = ApplyOrdering(filtered, sortBy, descending);
        int totalItems = filtered.Count();
        int totalPages = totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize);

        IReadOnlyList<UserAccountOverview> rows = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToOverview)
            .ToArray();

        return Task.FromResult(new PagedResult<UserAccountOverview>(rows, page, pageSize, totalItems, totalPages));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AdminPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AdminPlanDto> rows = Plans
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
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

        if (!TryResolvePlan(planCode, out AdminPlanDto? plan) || plan is null)
        {
            return Task.FromResult(false);
        }

        account.PlanCode = plan.Code;
        account.PlanName = plan.Name;
        account.MaxGps = plan.MaxGps;
        return Task.FromResult(true);
    }

    private static InMemoryUserAccount CreateAccount(Guid userId, string email, string? fullName, DateTimeOffset nowUtc)
    {
        if (!TryResolvePlan(DefaultPlanCode, out AdminPlanDto? plan) || plan is null)
        {
            throw new InvalidOperationException("default_plan_not_configured");
        }

        return new InMemoryUserAccount
        {
            UserId = userId,
            Email = email,
            FullName = fullName,
            PlanCode = plan.Code,
            PlanName = plan.Name,
            MaxGps = plan.MaxGps,
            CreatedAtUtc = nowUtc
        };
    }

    private static bool TryResolvePlan(string planCode, out AdminPlanDto? plan)
    {
        string normalizedCode = planCode.Trim().ToUpperInvariant();
        plan = Plans.FirstOrDefault(x => string.Equals(x.Code, normalizedCode, StringComparison.OrdinalIgnoreCase) && x.IsActive);
        return plan is not null;
    }

    private static UserAccountOverview ToOverview(InMemoryUserAccount account)
    {
        return new UserAccountOverview(
            account.UserId,
            account.Email,
            account.FullName,
            account.PlanCode,
            account.MaxGps,
            account.Devices.Count);
    }

    private static IEnumerable<InMemoryUserAccount> ApplyOrdering(
        IEnumerable<InMemoryUserAccount> source,
        string sortBy,
        bool descending)
    {
        return sortBy.ToLowerInvariant() switch
        {
            "fullname" => descending
                ? source.OrderByDescending(x => x.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.FullName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "plancode" => descending
                ? source.OrderByDescending(x => x.PlanCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.PlanCode, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "maxgps" => descending
                ? source.OrderByDescending(x => x.MaxGps).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.MaxGps).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "usedgps" => descending
                ? source.OrderByDescending(x => x.Devices.Count).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.Devices.Count).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            "createdat" => descending
                ? source.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Email, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? source.OrderByDescending(x => x.Email, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.UserId)
                : source.OrderBy(x => x.Email, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.UserId)
        };
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
