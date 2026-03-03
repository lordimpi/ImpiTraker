using Microsoft.Extensions.Configuration;

namespace ImpiTrack.DataAccess.Configuration;

/// <summary>
/// Resuelve cadenas de conexión por proveedor usando opciones explícitas y ConnectionStrings.
/// </summary>
public static class ProviderConnectionStringResolver
{
    /// <summary>
    /// Resuelve la cadena de conexión para la capa de datos de negocio.
    /// </summary>
    /// <param name="configuration">Configuración raíz de la aplicación.</param>
    /// <param name="provider">Proveedor seleccionado para persistencia.</param>
    /// <param name="explicitConnectionString">Cadena explícita desde <c>Database:ConnectionString</c>.</param>
    /// <returns>Cadena resuelta o <c>null</c> cuando no aplica.</returns>
    public static string? ResolveDatabaseConnectionString(
        IConfiguration configuration,
        DatabaseProvider provider,
        string? explicitConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        return provider switch
        {
            DatabaseProvider.SqlServer => configuration.GetConnectionString("SqlServer"),
            DatabaseProvider.Postgres => configuration.GetConnectionString("Postgres"),
            _ => null
        };
    }

    /// <summary>
    /// Resuelve la cadena de conexión para ASP.NET Identity.
    /// </summary>
    /// <param name="configuration">Configuración raíz de la aplicación.</param>
    /// <param name="provider">Proveedor de almacenamiento de Identity.</param>
    /// <param name="explicitConnectionString">Cadena explícita desde <c>IdentityStorage:ConnectionString</c>.</param>
    /// <returns>Cadena resuelta o <c>null</c> cuando no aplica.</returns>
    public static string? ResolveIdentityConnectionString(
        IConfiguration configuration,
        DatabaseProvider provider,
        string? explicitConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        return provider switch
        {
            DatabaseProvider.SqlServer =>
                configuration.GetConnectionString("IdentitySqlServer") ??
                configuration.GetConnectionString("SqlServer"),
            DatabaseProvider.Postgres =>
                configuration.GetConnectionString("IdentityPostgres") ??
                configuration.GetConnectionString("Postgres"),
            _ => null
        };
    }
}
