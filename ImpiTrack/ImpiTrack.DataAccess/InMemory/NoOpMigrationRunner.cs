using ImpiTrack.DataAccess.Abstractions;

namespace ImpiTrack.DataAccess.InMemory;

/// <summary>
/// Implementacion de migracion vacia para escenarios sin base de datos SQL.
/// </summary>
public sealed class NoOpMigrationRunner : IMigrationRunner
{
    /// <inheritdoc />
    public Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
