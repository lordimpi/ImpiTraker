using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Configuration;

/// <summary>
/// Opciones de bootstrap para crear usuario administrador inicial de Identity.
/// </summary>
public sealed class IdentityBootstrapOptions
{
    /// <summary>
    /// Nombre de la sección de configuración.
    /// </summary>
    public const string SectionName = "IdentityBootstrap";

    /// <summary>
    /// Indica si se debe sembrar usuario y rol administrador al iniciar.
    /// </summary>
    public bool SeedAdminOnStart { get; set; }

    /// <summary>
    /// Nombre de usuario del administrador inicial.
    /// </summary>
    [MinLength(3)]
    public string AdminUserName { get; set; } = "admin";

    /// <summary>
    /// Correo del administrador inicial.
    /// </summary>
    [EmailAddress]
    public string AdminEmail { get; set; } = "admin@imptrack.local";

    /// <summary>
    /// Contraseña del administrador inicial.
    /// </summary>
    [MinLength(8)]
    public string AdminPassword { get; set; } = "ChangeMe!123";

    /// <summary>
    /// Rol que se asigna al administrador inicial.
    /// </summary>
    [MinLength(3)]
    public string AdminRole { get; set; } = "Admin";

    /// <summary>
    /// Rol base que se asigna a los usuarios registrados.
    /// </summary>
    [MinLength(3)]
    public string UserRole { get; set; } = "User";
}
