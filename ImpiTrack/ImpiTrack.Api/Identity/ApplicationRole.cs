using Microsoft.AspNetCore.Identity;

namespace ImpiTrack.Api.Identity;

/// <summary>
/// Rol de aplicación para autorización con ASP.NET Identity.
/// </summary>
public sealed class ApplicationRole : IdentityRole<Guid>
{
}
