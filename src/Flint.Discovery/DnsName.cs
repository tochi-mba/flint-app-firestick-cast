using System.Text;

namespace Flint.Discovery;

/// <summary>
/// Reads and writes DNS names, including the compression pointers mDNS responders use heavily.
/// </summary>
/// <remarks>
/// Name compression is where a naive DNS parser becomes a denial-of-service target: a pointer can
/// point backwards into a name that points back again, and a parser that follows pointers without
/// a budget will loop forever on a hostile packet from the local network. The jump budget here is
/// the defence.
/// </remarks>
public static class DnsName
{
    /// <summary>Maximum length of a single label.</summary>
    public const int MaxLabelLength = 63;

    /// <summary>Maximum total length of an encoded name.</summary>
    public const int MaxNameLength = 255;

    /// <summary>
    /// Maximum compression pointers followed while reading one name.
    /// </summary>
    /// <remarks>
    /// Every pointer must move strictly backwards, so a packet can contain no more jumps than it
    /// has bytes. Capping well below that costs nothing legitimate and bounds a malicious loop.
    /// </remarks>
    public const int MaxPointerJumps = 64;

    /// <summary>
    /// Reads a name starting at <paramref name="offset"/>.
    /// </summary>
    /// <param name="packet">The whole packet, needed because pointers reference it absolutely.</param>
    /// <param name="offset">Where the name starts. Advanced past the name on return.</param>
    /// <returns>The dotted name, without a trailing dot.</returns>
    /// <exception cref="DnsFormatException">The name is malformed, looping, or overlong.</exception>
    public static string Read(ReadOnlySpan<byte> packet, ref int offset)
    {
        var builder = new StringBuilder();
        var jumps = 0;
        var position = offset;
        var followedPointer = false;
        var totalLength = 0;

        while (true)
        {
            if (position < 0 || position >= packet.Length)
            {
                throw new DnsFormatException("A name ran past the end of the packet.");
            }

            var length = packet[position];

            if ((length & 0xC0) == 0xC0)
            {
                if (position + 1 >= packet.Length)
                {
                    throw new DnsFormatException("A compression pointer was truncated.");
                }

                if (++jumps > MaxPointerJumps)
                {
                    throw new DnsFormatException("A name exceeded the compression-pointer budget.");
                }

                var target = ((length & 0x3F) << 8) | packet[position + 1];
                if (target >= position)
                {
                    throw new DnsFormatException("A compression pointer did not point backwards.");
                }

                if (!followedPointer)
                {
                    offset = position + 2;
                    followedPointer = true;
                }

                position = target;
                continue;
            }

            if (length == 0)
            {
                if (!followedPointer)
                {
                    offset = position + 1;
                }

                return builder.ToString();
            }

            if (length > MaxLabelLength)
            {
                throw new DnsFormatException($"A label of {length} bytes exceeds the 63-byte limit.");
            }

            if (position + 1 + length > packet.Length)
            {
                throw new DnsFormatException("A label ran past the end of the packet.");
            }

            totalLength += length + 1;
            if (totalLength > MaxNameLength)
            {
                throw new DnsFormatException("A name exceeded the 255-byte limit.");
            }

            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(Encoding.UTF8.GetString(packet.Slice(position + 1, length)));
            position += 1 + length;
        }
    }

    /// <summary>Writes a dotted name in wire format.</summary>
    /// <exception cref="DnsFormatException">A label is empty or too long.</exception>
    public static void Write(IList<byte> destination, string name)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(name);

        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length > MaxLabelLength)
            {
                throw new DnsFormatException($"Label '{label}' exceeds the 63-byte limit.");
            }

            destination.Add((byte)bytes.Length);
            foreach (var value in bytes)
            {
                destination.Add(value);
            }
        }

        destination.Add(0);
    }

    /// <summary>Compares two DNS names, which are case-insensitive and may carry a trailing dot.</summary>
    public static bool Equal(string left, string right) =>
        left.TrimEnd('.').Equals(right.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
}
