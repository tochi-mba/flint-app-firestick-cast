using System.Buffers.Binary;
using System.Text;

namespace Flint.Discovery;

/// <summary>
/// One ADB protocol message.
/// </summary>
/// <remarks>
/// The header is twenty-four bytes, all little-endian: command, two arguments, payload length,
/// payload checksum, then the command XOR <c>0xFFFFFFFF</c> as a magic value. The magic is the
/// cheapest way to reject a port that answered but is not speaking ADB, which matters because
/// Flint scans a range and will meet unrelated services on it.
/// </remarks>
/// <param name="Command">The command tag.</param>
/// <param name="Arg0">First argument. For a connect, the protocol version.</param>
/// <param name="Arg1">Second argument. For a connect, the maximum payload size.</param>
/// <param name="Payload">The message body.</param>
public sealed record AdbMessage(AdbCommand Command, uint Arg0, uint Arg1, byte[] Payload)
{
    /// <summary>Length of the fixed header.</summary>
    public const int HeaderLength = 24;

    /// <summary>
    /// The largest payload Flint will accept from a peer.
    /// </summary>
    /// <remarks>
    /// A device Flint has not authenticated with can claim any length. Capping it stops a hostile
    /// or broken peer on the local network from making Flint allocate without bound.
    /// </remarks>
    public const int MaxPayloadLength = 256 * 1024;

    /// <summary>The ADB protocol version Flint advertises.</summary>
    public const uint ProtocolVersion = 0x0100_0001;

    /// <summary>The maximum payload Flint advertises it can receive.</summary>
    public const uint MaxPayloadAdvertised = 256 * 1024;

    /// <summary>
    /// Builds the connect message that opens a probe.
    /// </summary>
    /// <remarks>
    /// The banner identifies Flint to the user on the television's authorisation prompt, so it is
    /// deliberately recognisable rather than a generic host string.
    /// </remarks>
    public static AdbMessage Connect() => new(
        AdbCommand.Connect,
        ProtocolVersion,
        MaxPayloadAdvertised,
        Encoding.UTF8.GetBytes("host::flint-rex-technologies\0"));

    /// <summary>Serialises this message, header first.</summary>
    public byte[] ToBytes()
    {
        var buffer = new byte[HeaderLength + Payload.Length];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], (uint)Command);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..8], Arg0);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..12], Arg1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..16], (uint)Payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..20], Checksum(Payload));
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..24], (uint)Command ^ 0xFFFF_FFFFu);
        Payload.CopyTo(span[HeaderLength..]);
        return buffer;
    }

    /// <summary>
    /// Reads a header, returning the command and the payload length still to be read.
    /// </summary>
    /// <param name="header">Exactly <see cref="HeaderLength"/> bytes.</param>
    /// <exception cref="ArgumentException">The header is the wrong length.</exception>
    /// <exception cref="AdbProtocolException">
    /// The magic does not match the command, or the declared payload exceeds
    /// <see cref="MaxPayloadLength"/>.
    /// </exception>
    public static AdbHeader ParseHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length != HeaderLength)
        {
            throw new ArgumentException(
                $"An ADB header is {HeaderLength} bytes, not {header.Length}.", nameof(header));
        }

        var command = BinaryPrimitives.ReadUInt32LittleEndian(header[..4]);
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header[20..24]);
        if ((command ^ 0xFFFF_FFFFu) != magic)
        {
            throw new AdbProtocolException(
                "Header magic does not match its command. This port is not speaking ADB.");
        }

        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(header[12..16]);
        if (payloadLength > MaxPayloadLength)
        {
            throw new AdbProtocolException(
                $"Declared payload of {payloadLength} bytes exceeds the {MaxPayloadLength}-byte cap.");
        }

        return new AdbHeader(
            (AdbCommand)command,
            BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]),
            (int)payloadLength,
            BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]));
    }

    /// <summary>
    /// The ADB payload checksum: an unsigned sum of every byte.
    /// </summary>
    /// <remarks>
    /// Not a CRC despite the field's name in the reference implementation. Newer devices send zero
    /// and ignore the field, so Flint computes it for outgoing messages but does not reject an
    /// incoming mismatch.
    /// </remarks>
    public static uint Checksum(ReadOnlySpan<byte> payload)
    {
        uint total = 0;
        foreach (var value in payload)
        {
            total += value;
        }

        return total;
    }
}
