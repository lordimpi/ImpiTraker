namespace ImpiTrack.Protocols.Abstractions;

/// <summary>
/// Identificador de correlacion asignado al ciclo de vida de una conexion TCP.
/// </summary>
public readonly record struct SessionId(Guid Value)
{
    /// <summary>
    /// Crea un nuevo identificador de sesion aleatorio.
    /// </summary>
    public static SessionId New() => new(Guid.NewGuid());

    /// <summary>
    /// Devuelve el identificador en formato compacto de cadena.
    /// </summary>
    public override string ToString() => Value.ToString("N");
}

/// <summary>
/// Identificador de correlacion asignado a un frame/paquete decodificado.
/// </summary>
public readonly record struct PacketId(Guid Value)
{
    /// <summary>
    /// Crea un nuevo identificador de paquete aleatorio.
    /// </summary>
    public static PacketId New() => new(Guid.NewGuid());

    /// <summary>
    /// Devuelve el identificador en formato compacto de cadena.
    /// </summary>
    public override string ToString() => Value.ToString("N");
}
