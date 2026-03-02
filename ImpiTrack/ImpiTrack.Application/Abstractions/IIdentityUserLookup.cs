namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Abstraccion para consultar usuarios de identidad desde Application.
/// </summary>
public interface IIdentityUserLookup
{
    /// <summary>
    /// Busca un usuario por su identificador.
    /// </summary>
    /// <param name="userId">Identificador del usuario.</param>
    /// <param name="cancellationToken">Token de cancelacion.</param>
    /// <returns>Informacion del usuario o <c>null</c> si no existe.</returns>
    Task<IdentityUserInfo?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);
}
