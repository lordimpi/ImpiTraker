namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Informacion minima de un usuario de identidad.
/// </summary>
/// <param name="UserId">Identificador del usuario.</param>
/// <param name="Email">Correo principal.</param>
public sealed record IdentityUserInfo(
    Guid UserId,
    string Email);
