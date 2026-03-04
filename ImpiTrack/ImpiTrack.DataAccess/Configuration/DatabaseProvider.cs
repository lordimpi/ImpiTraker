namespace ImpiTrack.DataAccess.Configuration;

/// <summary>
/// Proveedores de base de datos soportados por la capa de persistencia.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>
    /// Proveedor en memoria para pruebas o desarrollo sin base de datos.
    /// </summary>
    InMemory = 0,

    /// <summary>
    /// Motor Microsoft SQL Server.
    /// </summary>
    SqlServer = 1,

    /// <summary>
    /// Motor PostgreSQL.
    /// </summary>
    Postgres = 2
}
