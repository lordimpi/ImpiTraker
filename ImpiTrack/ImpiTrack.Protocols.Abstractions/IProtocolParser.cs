namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Parsea frames crudos para una familia de protocolo especifica.
/// </summary>
public interface IProtocolParser
{
    /// <summary>
    /// Obtiene el protocolo manejado por esta implementacion de parser.
    /// </summary>
    ProtocolId Protocol { get; }

    /// <summary>
    /// Intenta parsear el frame entregado en un mensaje normalizado.
    /// </summary>
    /// <param name="frame">Payload del frame decodificado.</param>
    /// <param name="message">Mensaje parseado cuando el parseo es exitoso.</param>
    /// <param name="error">Codigo de error cuando el parseo falla.</param>
    /// <returns><c>true</c> cuando el parseo es exitoso; de lo contrario <c>false</c>.</returns>
    bool TryParse(in Frame frame, out ParsedMessage? message, out string? error);
}
