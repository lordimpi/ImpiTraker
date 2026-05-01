namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Se lanza cuando un tipo de comando no esta soportado por el protocolo especificado.
/// </summary>
public sealed class CommandNotSupportedByProtocolException : Exception
{
    /// <summary>
    /// Tipo de comando que no esta soportado.
    /// </summary>
    public DeviceCommandType CommandType { get; }

    /// <summary>
    /// Protocolo que no soporta el comando.
    /// </summary>
    public ProtocolId Protocol { get; }

    /// <summary>
    /// Crea una nueva instancia de <see cref="CommandNotSupportedByProtocolException"/>.
    /// </summary>
    /// <param name="type">Tipo de comando no soportado.</param>
    /// <param name="protocol">Protocolo que rechaza el comando.</param>
    public CommandNotSupportedByProtocolException(DeviceCommandType type, ProtocolId protocol)
        : base($"El protocolo '{protocol}' no soporta el comando '{type}'.")
    {
        CommandType = type;
        Protocol = protocol;
    }
}

/// <summary>
/// Se lanza cuando un parametro requerido por el comando esta ausente en <c>Parameters</c>.
/// </summary>
public sealed class CommandParameterMissingException : Exception
{
    /// <summary>
    /// Nombre del campo requerido que no fue provisto.
    /// </summary>
    public string FieldName { get; }

    /// <summary>
    /// Crea una nueva instancia de <see cref="CommandParameterMissingException"/>.
    /// </summary>
    /// <param name="fieldName">Nombre del campo faltante.</param>
    public CommandParameterMissingException(string fieldName)
        : base($"Parametro requerido '{fieldName}' ausente en el comando.")
    {
        FieldName = fieldName;
    }
}
