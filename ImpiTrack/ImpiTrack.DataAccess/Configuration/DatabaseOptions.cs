using System.ComponentModel.DataAnnotations;

namespace ImpiTrack.DataAccess.Configuration;

/// <summary>
/// Opciones de persistencia para resolver proveedor, conexion y politicas de migracion.
/// </summary>
public sealed class DatabaseOptions
{
    /// <summary>
    /// Nombre de la seccion de configuracion.
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    /// Proveedor de base de datos. Valores validos: InMemory, SqlServer o Postgres.
    /// </summary>
    [Required]
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Cadena de conexion al proveedor configurado.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Timeout por comando SQL en segundos.
    /// </summary>
    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Indica si se deben ejecutar migraciones al iniciar el proceso.
    /// </summary>
    public bool EnableAutoMigrate { get; set; }
}
