using System.Buffers;
using System.Buffers.Binary;
using Flint.Protocol;

namespace Flint.Session.Browser;

/// <summary>Bounded frame reads/writes for the TLS browser stream.</summary>
internal static class BrowserFrameStream
{
    internal static async Task<WireFrame> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            await stream.ReadExactlyAsync(prefix.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            var bodyLength = BinaryPrimitives.ReadInt32BigEndian(prefix.AsSpan(0, 4));
            if (bodyLength < WireCodec.EnvelopeLength || bodyLength > WireCodec.MaxFrameLength)
            {
                throw new BrowserProtocolException($"Invalid secure browser frame length: {bodyLength}.");
            }

            var encodedLength = checked(4 + bodyLength);
            var encoded = ArrayPool<byte>.Shared.Rent(encodedLength);
            try
            {
                prefix.AsSpan(0, 4).CopyTo(encoded);
                await stream.ReadExactlyAsync(encoded.AsMemory(4, bodyLength), cancellationToken).ConfigureAwait(false);
                return WireCodec.Decode(encoded.AsSpan(0, encodedLength));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(encoded, clearArray: false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(prefix, clearArray: false);
        }
    }

    internal static Task WriteAsync(Stream stream, WireMessage message, CancellationToken cancellationToken) =>
        WriteAsync(stream, ProtocolVersion.Current, message, cancellationToken);

    internal static Task WriteAsync(
        Stream stream,
        int protocolVersion,
        WireMessage message,
        CancellationToken cancellationToken)
    {
        var encoded = WireCodec.Encode(new WireFrame(protocolVersion, message));
        return stream.WriteAsync(encoded, cancellationToken).AsTask();
    }
}
