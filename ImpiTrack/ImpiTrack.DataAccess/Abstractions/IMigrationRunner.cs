namespace ImpiTrack.DataAccess.Abstractions;

/// <summary>
/// Ejecuta migraciones SQL versionadas para el proveedor configurado.
/// </summary>
public interface IMigrationRunner
{
    /// <summary>
    /// Aplica scripts pendientes de migracion de forma idempotente.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    Task ApplyMigrationsAsync(CancellationToken cancellationToken);
}
