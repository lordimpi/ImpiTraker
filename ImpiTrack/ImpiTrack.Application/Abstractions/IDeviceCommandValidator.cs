using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Valida los parametros de un comando de dispositivo segun su tipo.
/// La validacion es independiente del protocolo y aplica reglas de rangos y campos requeridos.
/// </summary>
public interface IDeviceCommandValidator
{
    /// <summary>
    /// Valida los parametros del comando indicado.
    /// </summary>
    /// <param name="type">Tipo de comando.</param>
    /// <param name="parameters">Parametros recibidos del cliente. Null si no se enviaron.</param>
    /// <returns>
    /// Resultado con <c>IsValid=true</c> si los parametros son aceptables;
    /// caso contrario incluye un mensaje legible con la causa del rechazo.
    /// </returns>
    DeviceCommandValidationResult Validate(DeviceCommandType type, IDictionary<string, object>? parameters);
}

/// <summary>
/// Resultado de la validacion de parametros de un comando.
/// </summary>
/// <param name="IsValid">Indica si la validacion fue exitosa.</param>
/// <param name="ErrorMessage">Mensaje de error legible. Null si la validacion fue exitosa.</param>
public sealed record DeviceCommandValidationResult(bool IsValid, string? ErrorMessage)
{
    /// <summary>Resultado valido reutilizable.</summary>
    public static readonly DeviceCommandValidationResult Valid = new(true, null);

    /// <summary>
    /// Crea un resultado invalido con el mensaje indicado.
    /// </summary>
    /// <param name="message">Causa del rechazo.</param>
    public static DeviceCommandValidationResult Invalid(string message) => new(false, message);
}
