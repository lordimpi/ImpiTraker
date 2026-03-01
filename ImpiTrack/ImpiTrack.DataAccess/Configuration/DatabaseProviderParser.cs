namespace ImpiTrack.DataAccess.Configuration;

/// <summary>
/// Utilidades para normalizar y validar el proveedor de base de datos.
/// </summary>
public static class DatabaseProviderParser
{
    /// <summary>
    /// Intenta convertir texto de configuracion en un proveedor conocido.
    /// </summary>
    /// <param name="value">Valor textual proveniente de configuracion.</param>
    /// <param name="provider">Proveedor resultante cuando el parseo es exitoso.</param>
    /// <returns><c>true</c> cuando el proveedor es valido; de lo contrario <c>false</c>.</returns>
    public static bool TryParse(string? value, out DatabaseProvider provider)
    {
        provider = DatabaseProvider.InMemory;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (string.Equals(value, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            provider = DatabaseProvider.InMemory;
            return true;
        }

        if (string.Equals(value, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            provider = DatabaseProvider.SqlServer;
            return true;
        }

        if (string.Equals(value, "Postgres", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            provider = DatabaseProvider.Postgres;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Convierte texto de configuracion en un proveedor conocido.
    /// </summary>
    /// <param name="value">Valor textual proveniente de configuracion.</param>
    /// <returns>Proveedor normalizado.</returns>
    /// <exception cref="InvalidOperationException">Se lanza cuando el proveedor no es soportado.</exception>
    public static DatabaseProvider Parse(string? value)
    {
        if (TryParse(value, out DatabaseProvider provider))
        {
            return provider;
        }

        throw new InvalidOperationException($"database_provider_not_supported provider={value}");
    }
}
