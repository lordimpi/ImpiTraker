using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.Shared.Options;
using Microsoft.Extensions.Configuration;

namespace ImpiTrack.DataAccess.Connection;

/// <summary>
/// Estado normalizado de configuracion de base de datos para uso interno.
/// </summary>
public sealed class DatabaseRuntimeContext
{
    /// <summary>
    /// Crea un contexto de runtime a partir de opciones enlazadas.
    /// </summary>
    /// <param name="configuration">Configuración raíz para resolver ConnectionStrings por proveedor.</param>
    /// <param name="optionsService">Servicio de opciones tipadas.</param>
    public DatabaseRuntimeContext(
        IConfiguration configuration,
        IGenericOptionsService<DatabaseOptions> optionsService)
    {
        DatabaseOptions value = optionsService.GetOptions();
        Provider = DatabaseProviderParser.Parse(value.Provider);
        ConnectionString = ProviderConnectionStringResolver.ResolveDatabaseConnectionString(
            configuration,
            Provider,
            value.ConnectionString);
        CommandTimeoutSeconds = Math.Max(1, value.CommandTimeoutSeconds);
        EnableAutoMigrate = value.EnableAutoMigrate;
    }

    /// <summary>
    /// Proveedor seleccionado.
    /// </summary>
    public DatabaseProvider Provider { get; }

    /// <summary>
    /// Cadena de conexion configurada.
    /// </summary>
    public string? ConnectionString { get; }

    /// <summary>
    /// Timeout SQL en segundos.
    /// </summary>
    public int CommandTimeoutSeconds { get; }

    /// <summary>
    /// Indica si se debe migrar automaticamente al iniciar.
    /// </summary>
    public bool EnableAutoMigrate { get; }

    /// <summary>
    /// Indica si la persistencia SQL esta habilitada y tiene conexion valida.
    /// </summary>
    public bool IsSqlEnabled =>
        Provider is DatabaseProvider.SqlServer or DatabaseProvider.Postgres &&
        !string.IsNullOrWhiteSpace(ConnectionString);
}
