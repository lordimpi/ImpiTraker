using Microsoft.AspNetCore.Identity;

namespace ImpiTrack.Auth.Infrastructure.Identity;

/// <summary>
/// Usuario de aplicación para autenticación y autorización con ASP.NET Identity.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
}
