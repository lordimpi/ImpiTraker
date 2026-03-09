namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Estado del proceso de restablecimiento de contrasena.
/// </summary>
public enum ResetPasswordStatus
{
    /// <summary>
    /// La contrasena fue actualizada correctamente.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// La solicitud es invalida o el token no coincide con la cuenta.
    /// </summary>
    InvalidRequest = 2,

    /// <summary>
    /// El restablecimiento fallo por validaciones de Identity u otras reglas.
    /// </summary>
    Failed = 3
}

/// <summary>
/// Resultado de la operacion de restablecimiento de contrasena.
/// </summary>
/// <param name="Status">Estado final del proceso.</param>
/// <param name="Errors">Errores de validacion asociados.</param>
public sealed record ResetPasswordResult(
    ResetPasswordStatus Status,
    IReadOnlyList<string> Errors);
