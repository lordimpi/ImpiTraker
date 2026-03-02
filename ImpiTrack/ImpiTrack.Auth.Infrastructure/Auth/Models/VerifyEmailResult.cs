namespace ImpiTrack.Auth.Infrastructure.Auth.Models;

/// <summary>
/// Estado del proceso de verificación de correo.
/// </summary>
public enum VerifyEmailStatus
{
    /// <summary>
    /// Correo confirmado correctamente.
    /// </summary>
    Verified = 1,

    /// <summary>
    /// La cuenta ya se encontraba confirmada.
    /// </summary>
    AlreadyVerified = 2,

    /// <summary>
    /// No se encontró el usuario solicitado.
    /// </summary>
    UserNotFound = 3,

    /// <summary>
    /// El token de confirmación no es válido.
    /// </summary>
    InvalidToken = 4
}

/// <summary>
/// Resultado de la operación de confirmación de correo.
/// </summary>
/// <param name="Status">Estado final de verificación.</param>
/// <param name="Errors">Errores de validación asociados.</param>
public sealed record VerifyEmailResult(
    VerifyEmailStatus Status,
    IReadOnlyList<string> Errors);
