using System.Threading.Channels;
using ImpiTrack.Auth.Infrastructure.Configuration;
using ImpiTrack.Auth.Infrastructure.Email.Contracts;
using ImpiTrack.Auth.Infrastructure.Email.Models;
using ImpiTrack.Shared.Options;

namespace ImpiTrack.Auth.Infrastructure.Email.Services;

/// <summary>
/// Implementacion en memoria de cola de correos basada en Channel bounded.
/// </summary>
public sealed class ChannelEmailDispatchQueue : IEmailDispatchQueue
{
    private readonly Channel<EmailMessage> _channel;
    private readonly int _enqueueTimeoutMs;

    /// <summary>
    /// Crea una cola de despacho de correos con capacidad acotada.
    /// </summary>
    /// <param name="dispatchOptionsService">Opciones de capacidad y timeout.</param>
    public ChannelEmailDispatchQueue(IGenericOptionsService<EmailDispatchOptions> dispatchOptionsService)
    {
        EmailDispatchOptions options = dispatchOptionsService.GetOptions();
        _enqueueTimeoutMs = options.EnqueueTimeoutMs;
        _channel = Channel.CreateBounded<EmailMessage>(new BoundedChannelOptions(options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    /// <inheritdoc />
    public async ValueTask<bool> EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_enqueueTimeoutMs);

        try
        {
            if (!await _channel.Writer.WaitToWriteAsync(timeoutCts.Token))
            {
                return false;
            }

            await _channel.Writer.WriteAsync(message, timeoutCts.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
