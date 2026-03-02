using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.Auth.Infrastructure.Configuration;

/// <summary>
/// Opciones de almacenamiento para ASP.NET Identity.
/// </summary>
public sealed class IdentityStorageOptions
{
    /// <summary>
    /// Nombre de la sección de configuración.
    /// </summary>
    public const string SectionName = "IdentityStorage";

    /// <summary>
    /// Proveedor de Identity. Valores válidos: SqlServer o InMemory.
    /// </summary>
    [Required]
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Cadena de conexión SQL cuando Provider es SqlServer.
    /// </summary>
    public string? ConnectionString { get; set; }
}
