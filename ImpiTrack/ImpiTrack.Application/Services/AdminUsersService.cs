using ImpiTrack.Application.Abstractions;

namespace ImpiTrack.Application.Services;

/// <summary>
/// Implementacion de casos de uso administrativos de usuarios.
/// </summary>
public sealed class AdminUsersService : IAdminUsersService
{
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly IIdentityUserLookup _identityUserLookup;

    /// <summary>
    /// Crea una instancia del servicio administrativo de usuarios.
    /// </summary>
    /// <param name="userAccountRepository">Repositorio de cuentas.</param>
    /// <param name="identityUserLookup">Consulta de usuarios de identidad.</param>
    public AdminUsersService(
        IUserAccountRepository userAccountRepository,
        IIdentityUserLookup identityUserLookup)
    {
        _userAccountRepository = userAccountRepository;
        _identityUserLookup = identityUserLookup;
    }

    /// <inheritdoc />
    public Task<PagedResult<UserAccountOverview>> GetUsersAsync(AdminUserListQuery query, CancellationToken cancellationToken)
    {
        return _userAccountRepository.GetUsersAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AdminPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        return _userAccountRepository.GetPlansAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UserAccountSummary?> GetUserSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        bool exists = await EnsureProvisionedAsync(userId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await _userAccountRepository.GetUserSummaryAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserDeviceBinding>?> GetUserDevicesAsync(Guid userId, CancellationToken cancellationToken)
    {
        bool exists = await EnsureProvisionedAsync(userId, cancellationToken);
        if (!exists)
        {
            return null;
        }

        return await _userAccountRepository.GetUserDevicesAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SetUserPlanResult> SetUserPlanAsync(Guid userId, string planCode, CancellationToken cancellationToken)
    {
        bool exists = await EnsureProvisionedAsync(userId, cancellationToken);
        if (!exists)
        {
            return new SetUserPlanResult(SetUserPlanStatus.UserNotFound, null);
        }

        bool updated = await _userAccountRepository.SetUserPlanAsync(
            userId,
            planCode.Trim(),
            DateTimeOffset.UtcNow,
            cancellationToken);

        if (!updated)
        {
            return new SetUserPlanResult(SetUserPlanStatus.InvalidPlanCode, null);
        }

        UserAccountSummary? summary = await _userAccountRepository.GetUserSummaryAsync(userId, cancellationToken);
        return new SetUserPlanResult(SetUserPlanStatus.Updated, summary);
    }

    /// <inheritdoc />
    public async Task<AdminBindDeviceResult> BindDeviceAsync(Guid userId, string imei, CancellationToken cancellationToken)
    {
        bool exists = await EnsureProvisionedAsync(userId, cancellationToken);
        if (!exists)
        {
            return new AdminBindDeviceResult(AdminBindDeviceStatus.UserNotFound, null);
        }

        BindDeviceResult binding = await _userAccountRepository.BindDeviceAsync(
            userId,
            imei.Trim(),
            DateTimeOffset.UtcNow,
            cancellationToken);

        return new AdminBindDeviceResult(AdminBindDeviceStatus.Completed, binding);
    }

    /// <inheritdoc />
    public async Task<UnbindDeviceStatus> UnbindDeviceAsync(
        Guid userId,
        string imei,
        CancellationToken cancellationToken)
    {
        bool exists = await EnsureProvisionedAsync(userId, cancellationToken);
        if (!exists)
        {
            return UnbindDeviceStatus.UserNotFound;
        }

        bool removed = await _userAccountRepository.UnbindDeviceAsync(
            userId,
            imei.Trim(),
            DateTimeOffset.UtcNow,
            cancellationToken);

        return removed ? UnbindDeviceStatus.Removed : UnbindDeviceStatus.BindingNotFound;
    }

    private async Task<bool> EnsureProvisionedAsync(Guid userId, CancellationToken cancellationToken)
    {
        IdentityUserInfo? user = await _identityUserLookup.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        await _userAccountRepository.EnsureUserProvisioningAsync(
            user.UserId,
            user.Email,
            fullName: null,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return true;
    }
}
