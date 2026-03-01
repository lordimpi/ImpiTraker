using System.Data;
using System.Reflection;
using Dapper;
using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Configuration;
using ImpiTrack.DataAccess.Connection;

namespace ImpiTrack.DataAccess.Migrations;

/// <summary>
/// Ejecuta scripts SQL embebidos y registra versiones aplicadas de forma idempotente.
/// </summary>
public sealed class SqlScriptMigrationRunner : IMigrationRunner
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly DatabaseRuntimeContext _context;
    private readonly IReadOnlyList<MigrationScript> _scripts;

    /// <summary>
    /// Crea un ejecutor de migraciones para el proveedor configurado.
    /// </summary>
    /// <param name="connectionFactory">Fabrica de conexiones SQL abiertas.</param>
    /// <param name="context">Contexto de runtime de base de datos.</param>
    public SqlScriptMigrationRunner(IDbConnectionFactory connectionFactory, DatabaseRuntimeContext context)
    {
        _connectionFactory = connectionFactory;
        _context = context;
        _scripts = LoadScripts(context.Provider);
    }

    /// <inheritdoc />
    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        if (!_context.IsSqlEnabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await EnsureMigrationsTableAsync(connection, cancellationToken);

        IReadOnlySet<string> applied = await GetAppliedVersionsAsync(connection, cancellationToken);
        foreach (MigrationScript script in _scripts)
        {
            if (applied.Contains(script.Version))
            {
                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            try
            {
                CommandDefinition command = new(
                    script.Sql,
                    transaction: transaction,
                    commandTimeout: _context.CommandTimeoutSeconds,
                    cancellationToken: cancellationToken);

                await connection.ExecuteAsync(command);

                CommandDefinition register = new(
                    GetInsertMigrationSql(_context.Provider),
                    new { script.Version },
                    transaction,
                    _context.CommandTimeoutSeconds,
                    cancellationToken: cancellationToken);

                await connection.ExecuteAsync(register);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
    }

    private async Task EnsureMigrationsTableAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        CommandDefinition command = new(
            GetEnsureMigrationTableSql(_context.Provider),
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }

    private async Task<IReadOnlySet<string>> GetAppliedVersionsAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        CommandDefinition command = new(
            "SELECT version FROM schema_migrations;",
            commandTimeout: _context.CommandTimeoutSeconds,
            cancellationToken: cancellationToken);

        IEnumerable<string> versions = await connection.QueryAsync<string>(command);
        return versions.ToHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<MigrationScript> LoadScripts(DatabaseProvider provider)
    {
        Assembly assembly = typeof(SqlScriptMigrationRunner).Assembly;
        string folder = provider switch
        {
            DatabaseProvider.SqlServer => ".db.sqlserver.",
            DatabaseProvider.Postgres => ".db.postgres.",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(folder))
        {
            return [];
        }

        IEnumerable<string> names = assembly
            .GetManifestResourceNames()
            .Where(x => x.Contains(folder, StringComparison.OrdinalIgnoreCase) && x.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        var scripts = new List<MigrationScript>();
        foreach (string resourceName in names)
        {
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            string sql = reader.ReadToEnd();

            string fileName = resourceName[(resourceName.LastIndexOf(folder, StringComparison.OrdinalIgnoreCase) + folder.Length)..];
            string version = fileName.Replace(".sql", string.Empty, StringComparison.OrdinalIgnoreCase);
            scripts.Add(new MigrationScript(version, sql));
        }

        return scripts;
    }

    private static string GetEnsureMigrationTableSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                """
                IF OBJECT_ID('schema_migrations', 'U') IS NULL
                BEGIN
                    CREATE TABLE schema_migrations (
                        version NVARCHAR(128) NOT NULL PRIMARY KEY,
                        applied_at_utc DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
                    );
                END;
                """,
            DatabaseProvider.Postgres =>
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version VARCHAR(128) PRIMARY KEY,
                    applied_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
                );
                """,
            _ => string.Empty
        };
    }

    private static string GetInsertMigrationSql(DatabaseProvider provider)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer =>
                "INSERT INTO schema_migrations(version, applied_at_utc) VALUES (@Version, SYSUTCDATETIME());",
            DatabaseProvider.Postgres =>
                "INSERT INTO schema_migrations(version, applied_at_utc) VALUES (@Version, NOW());",
            _ => string.Empty
        };
    }

    private sealed record MigrationScript(string Version, string Sql);
}
