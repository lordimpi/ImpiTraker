using System.Buffers;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Tcp.Core.Framing;

/// <summary>
/// Decodifica frames desde una secuencia continua usando uno o mas delimitadores de bytes.
/// </summary>
public sealed class DelimiterFrameDecoder : IFrameDecoder
{
    private readonly byte[] _delimiters;
    private readonly int _maxFrameBytes;

    /// <summary>
    /// Crea un decoder de frames basado en delimitadores.
    /// </summary>
    /// <param name="delimiters">Bytes delimitadores de fin de frame permitidos.</param>
    /// <param name="maxFrameBytes">Tamano maximo aceptado para un frame.</param>
    public DelimiterFrameDecoder(byte[] delimiters, int maxFrameBytes)
    {
        _delimiters = delimiters;
        _maxFrameBytes = maxFrameBytes;
    }

    /// <inheritdoc />
    public bool TryReadFrame(ref ReadOnlySequence<byte> buffer, out Frame frame)
    {
        frame = default!;

        SequencePosition? delimiterPosition = FindFirstDelimiter(buffer, out byte delimiter);
        if (!delimiterPosition.HasValue)
        {
            if (buffer.Length > _maxFrameBytes)
            {
                throw new InvalidDataException("frame_too_large_without_delimiter");
            }

            return false;
        }

        long frameLength = buffer.Slice(0, delimiterPosition.Value).Length + 1;
        if (frameLength > _maxFrameBytes)
        {
            throw new InvalidDataException("frame_too_large");
        }

        SequencePosition consumed = buffer.GetPosition(1, delimiterPosition.Value);
        ReadOnlyMemory<byte> payload = buffer.Slice(0, consumed).ToArray();
        buffer = buffer.Slice(consumed);

        if (payload.Length == 1 && payload.Span[0] == delimiter)
        {
            return TryReadFrame(ref buffer, out frame);
        }

        frame = new Frame(payload, DateTimeOffset.UtcNow);
        return true;
    }

    private SequencePosition? FindFirstDelimiter(ReadOnlySequence<byte> buffer, out byte delimiter)
    {
        delimiter = default;

        SequencePosition? earliest = null;
        long earliestOffset = long.MaxValue;
        foreach (byte candidate in _delimiters)
        {
            SequencePosition? current = buffer.PositionOf(candidate);
            if (!current.HasValue)
            {
                continue;
            }

            long offset = buffer.Slice(0, current.Value).Length;
            if (offset < earliestOffset)
            {
                earliestOffset = offset;
                earliest = current;
                delimiter = candidate;
            }
        }

        return earliest;
    }
}
