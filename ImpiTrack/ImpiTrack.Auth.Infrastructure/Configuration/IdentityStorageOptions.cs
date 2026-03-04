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
    /// Proveedor de Identity. Valores validos: SqlServer o InMemory.
    /// Postgres no esta soportado en net10 estable para Identity.
    /// </summary>
    [Required]
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Cadena de conexión explícita para el proveedor seleccionado.
    /// Si está vacía, se resuelve desde ConnectionStrings.
    /// </summary>
    public string? ConnectionString { get; set; }
}
