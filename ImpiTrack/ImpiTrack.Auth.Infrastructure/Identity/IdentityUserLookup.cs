using ImpiTrack.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace ImpiTrack.Auth.Infrastructure.Identity;

/// <summary>
/// Adaptador de Identity para consultas de usuario requeridas por Application.
/// </summary>
public sealed class IdentityUserLookup : IIdentityUserLookup
{
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary>
    /// Crea un adaptador de consulta para usuarios de Identity.
    /// </summary>
    /// <param name="userManager">Administrador de usuarios.</param>
    public IdentityUserLookup(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<IdentityUserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationUser? user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        string fallbackEmail = $"{user.Id:N}@imptrack.local";
        return new IdentityUserInfo(user.Id, user.Email ?? fallbackEmail);
    }
}
