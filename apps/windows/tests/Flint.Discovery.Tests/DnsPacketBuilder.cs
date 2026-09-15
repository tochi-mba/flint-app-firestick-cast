using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace Flint.Discovery.Tests;

/// <summary>
/// Builds mDNS response packets by hand, so the codec is tested against bytes rather than against
/// its own writer.
/// </summary>
/// <remarks>
/// A round-trip test that encodes and decodes with the same code proves only self-consistency. A
/// responder on a real network will not use Flint's writer, so these fixtures are assembled
/// independently, including the compression pointers real responders emit.
/// </remarks>
internal sealed class DnsPacketBuilder
{
    private readonly List<byte> _records = [];
    private int _answerCount;

    internal DnsPacketBuilder AddPointer(string name, string target)
    {
        WriteName(name);
        WriteRecordHeader(DnsRecordType.Ptr, TargetBytes(target).Length);
        _records.AddRange(TargetBytes(target));
        _answerCount++;
        return this;
    }

    internal DnsPacketBuilder AddService(string name, string host, int port)
    {
        var target = TargetBytes(host);
        WriteName(name);
        WriteRecordHeader(DnsRecordType.Srv, 6 + target.Length);
        _records.AddRange([0, 0]);                                  // Priority.
        _records.AddRange([0, 0]);                                  // Weight.
        _records.AddRange([(byte)(port >> 8), (byte)(port & 0xFF)]);
        _records.AddRange(target);
        _answerCount++;
        return this;
    }

    internal DnsPacketBuilder AddAddress(string host, string address)
    {
        WriteName(host);
        WriteRecordHeader(DnsRecordType.A, 4);
        _records.AddRange(IPAddress.Parse(address).GetAddressBytes());
        _answerCount++;
        return this;
    }

    internal DnsPacketBuilder AddText(string name, params string[] entries)
    {
        var body = new List<byte>();
        foreach (var entry in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(entry);
            body.Add((byte)bytes.Length);
            body.AddRange(bytes);
        }

        WriteName(name);
        WriteRecordHeader(DnsRecordType.Txt, body.Count);
        _records.AddRange(body);
        _answerCount++;
        return this;
    }

    /// <summary>
    /// Adds a TXT record from raw length-prefix bytes, used to prove malformed UTF-8 handling.
    /// </summary>
    internal DnsPacketBuilder AddRawText(string name, params byte[] textData)
    {
        WriteName(name);
        WriteRecordHeader(DnsRecordType.Txt, textData.Length);
        _records.AddRange(textData);
        _answerCount++;
        return this;
    }

    /// <summary>Assembles the packet with a response header.</summary>
    internal byte[] Build()
    {
        var packet = new List<byte>(12 + _records.Count);
        packet.AddRange([0, 0]);                                        // Transaction id.
        packet.AddRange([0x84, 0x00]);                                  // Authoritative response.
        packet.AddRange([0, 0]);                                        // No questions.
        packet.AddRange([(byte)(_answerCount >> 8), (byte)(_answerCount & 0xFF)]);
        packet.AddRange([0, 0, 0, 0]);                                  // No authority or additional.
        packet.AddRange(_records);
        return [.. packet];
    }

    /// <summary>A response header claiming more records than the body contains.</summary>
    internal byte[] BuildWithAnswerCount(int claimed)
    {
        var packet = Build();
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(6, 2), (ushort)claimed);
        return packet;
    }

    private void WriteName(string name) => _records.AddRange(TargetBytes(name));

    private void WriteRecordHeader(DnsRecordType type, int dataLength)
    {
        _records.AddRange([0, (byte)type]);
        _records.AddRange([0, 1]);                                      // Class IN.
        _records.AddRange([0, 0, 0, 120]);                              // TTL.
        _records.AddRange([(byte)(dataLength >> 8), (byte)(dataLength & 0xFF)]);
    }

    private static byte[] TargetBytes(string name)
    {
        var bytes = new List<byte>();
        foreach (var label in name.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var encoded = Encoding.UTF8.GetBytes(label);
            bytes.Add((byte)encoded.Length);
            bytes.AddRange(encoded);
        }

        bytes.Add(0);
        return [.. bytes];
    }
}
