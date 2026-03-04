using System.Data.Common;

namespace ImpiTrack.DataAccess.Abstractions;

/// <summary>
/// Fabrica conexiones abiertas hacia la base de datos configurada.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Crea y abre una conexion para operaciones de lectura/escritura.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelacion de la operacion.</param>
    /// <returns>Conexion abierta lista para ejecutar comandos.</returns>
    Task<DbConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken);
}
