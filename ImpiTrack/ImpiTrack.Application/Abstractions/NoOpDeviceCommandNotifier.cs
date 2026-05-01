namespace ImpiTrack.Application.Abstractions;

/// <summary>
/// Implementacion no-op de <see cref="IDeviceCommandNotifier"/> usada como default en entornos
/// sin SignalR (TcpServer standalone, tests). La implementacion real (SignalR) se registra en
/// <c>ImpiTrack.Api.Program.cs</c> reemplazando este registro.
/// </summary>
public sealed class NoOpDeviceCommandNotifier : IDeviceCommandNotifier
{
    /// <inheritdoc />
    public Task NotifyStatusChangedAsync(Guid userId, DeviceCommandRecord command, CancellationToken ct)
        => Task.CompletedTask;
}
