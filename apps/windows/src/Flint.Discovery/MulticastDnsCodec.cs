using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Flint.Discovery;

/// <summary>
/// Builds mDNS queries and extracts service instances from responses.
/// </summary>
/// <remarks>
/// Deliberately narrow: Flint needs to ask "who offers this service type" and read back the PTR,
/// SRV, TXT and A records that answer it. It is not a general resolver, and every parse path
/// tolerates records it does not understand rather than rejecting the packet, because responders on
/// a home network advertise a great deal that has nothing to do with Fire TV.
/// </remarks>
public static class MulticastDnsCodec
{
    /// <summary>The service type Fire TV devices advertise for the Amazon Whisperplay protocol.</summary>
    public const string FireTvServiceType = "_amzn-wplay._tcp.local";

    /// <summary>The multicast address for mDNS.</summary>
    public static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");

    /// <summary>The mDNS port.</summary>
    public const int MulticastPort = 5353;

    /// <summary>
    /// The meta-query that asks a responder to list every service type it offers.
    /// </summary>
    public const string ServiceEnumerationType = "_services._dns-sd._udp.local";

    private const int HeaderLength = 12;
    private const ushort ResponseFlag = 0x8000;

    /// <summary>
    /// Builds a query for every instance of a service type.
    /// </summary>
    /// <remarks>
    /// The transaction identifier is zero, as mDNS requires. The unicast-response bit is left clear
    /// so responders answer to the multicast group, which lets Flint see devices that answered a
    /// query from some other host too.
    /// </remarks>
    public static byte[] BuildQuery(string serviceType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType);

        var packet = new List<byte>(64);
        packet.AddRange([0, 0]);                     // Transaction id: always zero for mDNS.
        packet.AddRange([0, 0]);                     // Flags: a standard query.
        packet.AddRange([0, 1]);                     // One question.
        packet.AddRange([0, 0, 0, 0, 0, 0]);         // No answer, authority or additional records.

        DnsName.Write(packet, serviceType);
        packet.AddRange([0, (byte)DnsRecordType.Ptr]);
        packet.AddRange([0, 1]);                     // Class IN.

        return [.. packet];
    }

    /// <summary>
    /// Reads the PTR targets in a packet, whatever they point at.
    /// </summary>
    /// <remarks>
    /// Used for the service-enumeration meta-query, where the targets are service type names rather
    /// than instances and no SRV or A record accompanies them. Separate from
    /// <see cref="ReadInstances"/> because that method deliberately drops a pointer it cannot
    /// resolve to an address, which is exactly what every answer here would be.
    /// </remarks>
    public static IReadOnlyList<string> ReadPointerTargets(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderLength)
        {
            throw new DnsFormatException("A DNS packet is at least 12 bytes.");
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        if ((flags & ResponseFlag) == 0)
        {
            return [];
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..6]);
        var recordCount = BinaryPrimitives.ReadUInt16BigEndian(packet[6..8])
            + BinaryPrimitives.ReadUInt16BigEndian(packet[8..10])
            + BinaryPrimitives.ReadUInt16BigEndian(packet[10..12]);

        var offset = HeaderLength;
        for (var index = 0; index < questions; index++)
        {
            DnsName.Read(packet, ref offset);
            offset += 4;
            if (offset > packet.Length)
            {
                throw new DnsFormatException("A question ran past the end of the packet.");
            }
        }

        var targets = new List<string>();
        for (var index = 0; index < recordCount && offset < packet.Length; index++)
        {
            DnsName.Read(packet, ref offset);
            if (offset + 10 > packet.Length)
            {
                throw new DnsFormatException("A record header ran past the end of the packet.");
            }

            var type = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset, 2));
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 8, 2));
            offset += 10;

            if (offset + dataLength > packet.Length)
            {
                throw new DnsFormatException("Record data ran past the end of the packet.");
            }

            if (type == DnsRecordType.Ptr)
            {
                var target = offset;
                targets.Add(DnsName.Read(packet, ref target));
            }

            offset += dataLength;
        }

        return targets;
    }

    /// <summary>
    /// Extracts every instance of <paramref name="serviceType"/> that the packet fully describes.
    /// </summary>
    /// <remarks>
    /// An instance is only returned when the packet carries enough to reach it — an SRV record for
    /// the host and port, and an A record for the address. A PTR alone names something Flint cannot
    /// connect to, so it is dropped rather than reported as a half-found device.
    /// </remarks>
    /// <exception cref="DnsFormatException">The packet is not readable as DNS.</exception>
    public static IReadOnlyList<ServiceInstance> ReadInstances(ReadOnlySpan<byte> packet, string serviceType)
    {
        if (packet.Length < HeaderLength)
        {
            throw new DnsFormatException("A DNS packet is at least 12 bytes.");
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(packet[2..4]);
        if ((flags & ResponseFlag) == 0)
        {
            return [];
        }

        var questions = BinaryPrimitives.ReadUInt16BigEndian(packet[4..6]);
        var recordCount = BinaryPrimitives.ReadUInt16BigEndian(packet[6..8])
            + BinaryPrimitives.ReadUInt16BigEndian(packet[8..10])
            + BinaryPrimitives.ReadUInt16BigEndian(packet[10..12]);

        var offset = HeaderLength;
        for (var index = 0; index < questions; index++)
        {
            DnsName.Read(packet, ref offset);
            offset += 4;
            if (offset > packet.Length)
            {
                throw new DnsFormatException("A question ran past the end of the packet.");
            }
        }

        var pointers = new List<string>();
        var services = new Dictionary<string, (string Host, int Port)>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
        var textRecords = new Dictionary<string, TextRecordAccumulator>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < recordCount && offset < packet.Length; index++)
        {
            var name = DnsName.Read(packet, ref offset);
            if (offset + 10 > packet.Length)
            {
                throw new DnsFormatException("A record header ran past the end of the packet.");
            }

            var type = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset, 2));
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(offset + 8, 2));
            offset += 10;

            if (offset + dataLength > packet.Length)
            {
                throw new DnsFormatException("Record data ran past the end of the packet.");
            }

            var dataStart = offset;
            switch (type)
            {
                case DnsRecordType.Ptr when DnsName.Equal(name, serviceType):
                    var target = dataStart;
                    pointers.Add(DnsName.Read(packet, ref target));
                    break;

                case DnsRecordType.Srv when dataLength >= 7:
                    var port = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(dataStart + 4, 2));
                    var hostOffset = dataStart + 6;
                    services[name] = (DnsName.Read(packet, ref hostOffset), port);
                    break;

                case DnsRecordType.A when dataLength == 4:
                    addresses[name] = new IPAddress(packet.Slice(dataStart, 4).ToArray());
                    break;

                case DnsRecordType.Txt:
                    if (!textRecords.TryGetValue(name, out var accumulator))
                    {
                        accumulator = new TextRecordAccumulator();
                        textRecords[name] = accumulator;
                    }

                    accumulator.Append(packet.Slice(dataStart, dataLength));
                    break;

                default:
                    break;
            }

            offset = dataStart + dataLength;
        }

        return BuildInstances(serviceType, pointers, services, addresses, textRecords);
    }

    private static List<ServiceInstance> BuildInstances(
        string serviceType,
        List<string> pointers,
        Dictionary<string, (string Host, int Port)> services,
        Dictionary<string, IPAddress> addresses,
        Dictionary<string, TextRecordAccumulator> textRecords)
    {
        var suffix = "." + serviceType;
        var results = new List<ServiceInstance>();

        foreach (var pointer in pointers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!services.TryGetValue(pointer, out var service))
            {
                continue;
            }

            if (!addresses.TryGetValue(service.Host, out var address))
            {
                continue;
            }

            var canonical = pointer.TrimEnd('.');
            var instanceName = canonical.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? canonical[..^suffix.Length]
                : canonical;

            textRecords.TryGetValue(pointer, out var text);
            results.Add(new ServiceInstance(
                instanceName,
                address,
                service.Port,
                text?.Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                text?.RawEntries ?? [],
                text?.Malformed ?? false));
        }

        return results;
    }

    private sealed class TextRecordAccumulator
    {
        private readonly Dictionary<string, string> attributes = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> rawEntries = [];

        public IReadOnlyDictionary<string, string> Attributes => attributes;
        public IReadOnlyList<string> RawEntries => rawEntries;
        public bool Malformed { get; private set; }

        public void Append(ReadOnlySpan<byte> data)
        {
            var offset = 0;
            while (offset < data.Length)
            {
                var length = data[offset];
                offset++;
                if (offset + length > data.Length)
                {
                    Malformed = true;
                    break;
                }

                if (length == 0)
                {
                    continue;
                }

                var slice = data.Slice(offset, length);
                offset += length;
                string entry;
                try
                {
                    entry = Encoding.UTF8.GetString(slice);
                }
                catch (DecoderFallbackException)
                {
                    Malformed = true;
                    continue;
                }

                if (entry.Any(character => character == '\uFFFD') && ContainsInvalidUtf8(slice))
                {
                    Malformed = true;
                    continue;
                }

                // Reject truncated / invalid UTF-8 sequences that .NET replaces rather than throws.
                if (!IsValidUtf8(slice))
                {
                    Malformed = true;
                    continue;
                }

                rawEntries.Add(entry);
                var equals = entry.IndexOf('=', StringComparison.Ordinal);
                if (equals < 0)
                {
                    attributes[entry] = string.Empty;
                }
                else if (equals > 0)
                {
                    attributes[entry[..equals]] = entry[(equals + 1)..];
                }
            }
        }

        private static bool IsValidUtf8(ReadOnlySpan<byte> bytes)
        {
            try
            {
                Encoding.UTF8.GetString(bytes);
                // Re-encode and compare to catch replacement characters from invalid sequences.
                var roundTrip = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes));
                return roundTrip.AsSpan().SequenceEqual(bytes);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool ContainsInvalidUtf8(ReadOnlySpan<byte> bytes) => !IsValidUtf8(bytes);
    }
}
