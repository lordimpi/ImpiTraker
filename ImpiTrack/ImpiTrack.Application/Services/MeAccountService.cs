using ImpiTrack.Application.Abstractions;

namespace ImpiTrack.Application.Services;

/// <summary>
/// Implementacion de casos de uso de autogestion de cuenta.
/// </summary>
public sealed class MeAccountService : IMeAccountService
{
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly IIdentityUserLookup _identityUserLookup;

    /// <summary>
    /// Crea una instancia del servicio de cuenta de usuario.
    /// </summary>
    /// <param name="userAccountRepository">Repositorio de cuentas.</param>
    /// <param name="identityUserLookup">Consulta de usuarios de identidad.</param>
    public MeAccountService(
        IUserAccountRepository userAccountRepository,
        IIdentityUserLookup identityUserLookup)
    {
        _userAccountRepository = userAccountRepository;
        _identityUserLookup = identityUserLookup;
    }

    /// <inheritdoc />
    public async Task<UserAccountSummary?> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await GetOrProvisionSummaryAsync(userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<PagedResult<UserDeviceBinding>?> GetDevicesPagedAsync(Guid userId, MeDeviceListQuery query, CancellationToken cancellationToken)
    {
        UserAccountSummary? summary = await GetOrProvisionSummaryAsync(userId, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        return await _userAccountRepository.GetUserDevicesPagedMeAsync(userId, query, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<BindDeviceResult?> BindDeviceAsync(Guid userId, string imei, CancellationToken cancellationToken)
    {
        UserAccountSummary? summary = await GetOrProvisionSummaryAsync(userId, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        return await _userAccountRepository.BindDeviceAsync(
            userId,
            imei.Trim(),
            DateTimeOffset.UtcNow,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<UnbindDeviceStatus> UnbindDeviceAsync(
        Guid userId,
        string imei,
        CancellationToken cancellationToken)
    {
        UserAccountSummary? summary = await GetOrProvisionSummaryAsync(userId, cancellationToken);
        if (summary is null)
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

    /// <inheritdoc />
    public async Task<UpdateDeviceAliasStatus> UpdateDeviceAliasAsync(
        Guid userId,
        string imei,
        string? alias,
        CancellationToken cancellationToken)
    {
        UserAccountSummary? summary = await GetOrProvisionSummaryAsync(userId, cancellationToken);
        if (summary is null)
        {
            return UpdateDeviceAliasStatus.UserNotFound;
        }

        string? normalizedAlias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();
        if (normalizedAlias is not null && normalizedAlias.Length > 50)
        {
            return UpdateDeviceAliasStatus.AliasTooLong;
        }

        bool updated = await _userAccountRepository.UpdateDeviceAliasAsync(
            userId,
            imei.Trim(),
            normalizedAlias,
            cancellationToken);

        return updated ? UpdateDeviceAliasStatus.Updated : UpdateDeviceAliasStatus.BindingNotFound;
    }

    private async Task<UserAccountSummary?> GetOrProvisionSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        UserAccountSummary? summary = await _userAccountRepository.GetUserSummaryAsync(userId, cancellationToken);
        if (summary is not null)
        {
            return summary;
        }

        IdentityUserInfo? user = await _identityUserLookup.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        await _userAccountRepository.EnsureUserProvisioningAsync(
            user.UserId,
            user.Email,
            fullName: null,
            DateTimeOffset.UtcNow,
            cancellationToken);

        return await _userAccountRepository.GetUserSummaryAsync(userId, cancellationToken);
    }
}
