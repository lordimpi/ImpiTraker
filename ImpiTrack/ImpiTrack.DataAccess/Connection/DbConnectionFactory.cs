using System.Data.Common;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace ImpiTrack.DataAccess.Connection;

/// <summary>
/// Fabrica conexiones SQL abiertas segun el proveedor configurado.
/// </summary>
public sealed class DbConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseRuntimeContext _context;

    /// <summary>
    /// Crea la fabrica de conexiones para el runtime actual.
    /// </summary>
    /// <param name="context">Contexto de configuracion normalizado.</param>
    public DbConnectionFactory(DatabaseRuntimeContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (!_context.IsSqlEnabled)
        {
            throw new InvalidOperationException("database_not_configured_for_sql");
        }

        DbConnection connection = _context.Provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(_context.ConnectionString),
            DatabaseProvider.Postgres => new NpgsqlConnection(_context.ConnectionString),
            _ => throw new InvalidOperationException("database_provider_requires_sql_connection")
        };

        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
