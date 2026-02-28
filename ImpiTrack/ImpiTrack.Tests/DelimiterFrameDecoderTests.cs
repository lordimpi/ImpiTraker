using System.Buffers;
using ImpiTrack.Tcp.Core.Framing;

namespace ImpiTrack.Tests;

public sealed class DelimiterFrameDecoderTests
{
    [Fact]
    public void TryReadFrame_ShouldHandleConcatenatedFrames()
    {
        var decoder = new DelimiterFrameDecoder([(byte)';'], 1024);
        ReadOnlySequence<byte> buffer = new("A;B;C;"u8.ToArray());

        bool first = decoder.TryReadFrame(ref buffer, out var frame1);
        bool second = decoder.TryReadFrame(ref buffer, out var frame2);
        bool third = decoder.TryReadFrame(ref buffer, out var frame3);
        bool fourth = decoder.TryReadFrame(ref buffer, out _);

        Assert.True(first);
        Assert.True(second);
        Assert.True(third);
        Assert.False(fourth);
        Assert.Equal("A;", frame1.Payload.Span.ToArray().AsSpan().ToStringSafe());
        Assert.Equal("B;", frame2.Payload.Span.ToArray().AsSpan().ToStringSafe());
        Assert.Equal("C;", frame3.Payload.Span.ToArray().AsSpan().ToStringSafe());
    }

    [Fact]
    public void TryReadFrame_ShouldReturnFalseForIncompleteFrame()
    {
        var decoder = new DelimiterFrameDecoder([(byte)';'], 1024);
        ReadOnlySequence<byte> buffer = new("A;B"u8.ToArray());

        bool first = decoder.TryReadFrame(ref buffer, out var frame1);
        bool second = decoder.TryReadFrame(ref buffer, out _);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal("A;", frame1.Payload.Span.ToArray().AsSpan().ToStringSafe());
        Assert.Equal("B", buffer.ToArray().AsSpan().ToStringSafe());
    }

    [Fact]
    public void TryReadFrame_ShouldThrowWhenFrameExceedsMaxFrameBytes()
    {
        var decoder = new DelimiterFrameDecoder([(byte)';'], 4);
        ReadOnlySequence<byte> buffer = new("ABCDE;"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => decoder.TryReadFrame(ref buffer, out _));
    }
}

internal static class ByteSpanExtensions
{
    public static string ToStringSafe(this ReadOnlySpan<byte> source)
    {
        return System.Text.Encoding.ASCII.GetString(source);
    }
}
