namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Estado del proceso de registro de usuario.
/// </summary>
public enum RegisterStatus
{
    /// <summary>
    /// Registro completado correctamente.
    /// </summary>
    Created = 1,

    /// <summary>
    /// El nombre de usuario ya se encuentra en uso.
    /// </summary>
    UserNameAlreadyExists = 2,

    /// <summary>
    /// El correo electrónico ya se encuentra en uso.
    /// </summary>
    EmailAlreadyExists = 3,

    /// <summary>
    /// El registro falló por validaciones u otras reglas de Identity.
    /// </summary>
    Failed = 4
}

/// <summary>
/// Resultado de la operación de registro.
/// </summary>
/// <param name="Status">Estado final del registro.</param>
/// <param name="Registration">Datos de registro cuando se crea el usuario.</param>
/// <param name="Errors">Errores de validación de la operación.</param>
public sealed record RegisterResult(
    RegisterStatus Status,
    RegisterResponse? Registration,
    IReadOnlyList<string> Errors);
