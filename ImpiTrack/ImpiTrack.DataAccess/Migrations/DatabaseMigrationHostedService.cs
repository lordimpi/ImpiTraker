using ImpiTrack.DataAccess.Abstractions;
using ImpiTrack.DataAccess.Connection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ImpiTrack.DataAccess.Migrations;

/// <summary>
/// Hosted service que ejecuta migraciones al arranque cuando esta habilitado.
/// </summary>
public sealed class DatabaseMigrationHostedService : IHostedService
{
    private readonly IMigrationRunner _migrationRunner;
    private readonly DatabaseRuntimeContext _context;
    private readonly ILogger<DatabaseMigrationHostedService> _logger;

    /// <summary>
    /// Crea un servicio de arranque para migraciones SQL.
    /// </summary>
    /// <param name="migrationRunner">Ejecutor de migraciones configurado.</param>
    /// <param name="context">Contexto de runtime de base de datos.</param>
    /// <param name="logger">Logger estructurado.</param>
    public DatabaseMigrationHostedService(
        IMigrationRunner migrationRunner,
        DatabaseRuntimeContext context,
        ILogger<DatabaseMigrationHostedService> logger)
    {
        _migrationRunner = migrationRunner;
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_context.IsSqlEnabled)
        {
            _logger.LogInformation("db_migrations_skipped reason={reason}", "provider_not_sql_or_connection_missing");
            return;
        }

        if (!_context.EnableAutoMigrate)
        {
            _logger.LogInformation("db_migrations_skipped reason={reason}", "auto_migrate_disabled");
            return;
        }

        _logger.LogInformation("db_migrations_start provider={provider}", _context.Provider);
        await _migrationRunner.ApplyMigrationsAsync(cancellationToken);
        _logger.LogInformation("db_migrations_completed provider={provider}", _context.Provider);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
