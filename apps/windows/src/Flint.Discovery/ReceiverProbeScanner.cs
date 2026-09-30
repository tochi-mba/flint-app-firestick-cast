using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Flint.Discovery;

/// <summary>Finds Flint receivers with the receiver's small, read-only TCP probe.</summary>
/// <remarks>
/// This is the fallback for links that filter multicast. It never scans arbitrary ports: only the
/// documented Flint receiver port is contacted, only on a derived local subnet, and a subnet larger
/// than the bounded budget is left alone.
/// </remarks>
internal static class ReceiverProbeScanner
{
    internal const int Port = 47_855;
    private const int MaximumHosts = 512;
    private const int Concurrency = 16;
    private const int TimeoutMilliseconds = 400;
    private const int MaximumResponseBytes = 512;
    private static readonly byte[] Request = Encoding.ASCII.GetBytes("REXCAST DISCOVER/1\n");

    internal sealed record Answer(IPAddress Address, string ModelName, int ServicePort);

    internal static async Task<IReadOnlyList<Answer>> ScanAsync(CancellationToken cancellationToken)
    {
        var scans = LocalSubnets().Select(subnet => ScanAsync(subnet, cancellationToken));
        var answers = await Task.WhenAll(scans).ConfigureAwait(false);
        return answers
            .SelectMany(static result => result)
            .DistinctBy(static answer => answer.Address)
            .ToArray();
    }

    internal static Answer? Parse(IPAddress address, ReadOnlySpan<byte> response)
    {
        if (response.Length == 0 || response.Length > MaximumResponseBytes)
        {
            return null;
        }

        var line = Encoding.UTF8.GetString(response).TrimEnd('\r', '\n');
        var fields = line.Split('\t');
        if (fields.Length != 3
            || !string.Equals(fields[0], "REXCAST RECEIVER/1", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(fields[1])
            || !int.TryParse(fields[2], out var port)
            || port is < 1 or > 65_535)
        {
            return null;
        }

        return new Answer(address, fields[1], port);
    }

    private static async Task<IReadOnlyList<Answer>> ScanAsync(
        LocalSubnet subnet,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Concurrency, Concurrency);
        var probes = subnet.Hosts().Select(async address =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ProbeAsync(subnet.Address, address, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });
        var answers = await Task.WhenAll(probes).ConfigureAwait(false);
        return answers.OfType<Answer>().ToArray();
    }

    private static async Task<Answer?> ProbeAsync(
        IPAddress local,
        IPAddress target,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeoutMilliseconds);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(local, 0));
            await socket.ConnectAsync(new IPEndPoint(target, Port), deadline.Token).ConfigureAwait(false);
            var sent = 0;
            while (sent < Request.Length)
            {
                sent += await socket.SendAsync(
                    Request.AsMemory(sent),
                    SocketFlags.None,
                    deadline.Token).ConfigureAwait(false);
            }

            var response = new byte[MaximumResponseBytes];
            var length = 0;
            var terminated = false;
            while (length < response.Length)
            {
                var read = await socket.ReceiveAsync(
                    response.AsMemory(length, 1),
                    SocketFlags.None,
                    deadline.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (response[length++] == (byte)'\n')
                {
                    terminated = true;
                    break;
                }
            }

            return terminated ? Parse(target, response.AsSpan(0, length)) : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static IEnumerable<LocalSubnet> LocalSubnets() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(static nic => nic.OperationalStatus == OperationalStatus.Up)
            .Where(static nic => nic.NetworkInterfaceType is not NetworkInterfaceType.Loopback
                and not NetworkInterfaceType.Tunnel
                and not NetworkInterfaceType.Ppp)
            .SelectMany(static nic => nic.GetIPProperties().UnicastAddresses)
            .Where(static unicast => unicast.Address.AddressFamily == AddressFamily.InterNetwork)
            .Where(static unicast => IsPrivate(unicast.Address))
            .Select(static unicast => new LocalSubnet(unicast.Address, unicast.PrefixLength))
            .Where(static subnet => subnet.HostCount is > 0 and <= MaximumHosts)
            .Distinct();

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }

    private sealed record LocalSubnet(IPAddress Address, int PrefixLength)
    {
        private uint Value => BinaryPrimitives.ReadUInt32BigEndian(Address.GetAddressBytes());
        private uint Mask => PrefixLength == 0 ? 0 : uint.MaxValue << (32 - PrefixLength);
        private uint First => (Value & Mask) + (PrefixLength <= 30 ? 1u : 0u);
        private uint Last => (Value | ~Mask) - (PrefixLength <= 30 ? 1u : 0u);

        internal long HostCount => Last < First
            ? 0
            : (long)Last - First + 1 - (Value >= First && Value <= Last ? 1 : 0);

        internal IEnumerable<IPAddress> Hosts()
        {
            for (var value = First; value <= Last; value++)
            {
                if (value != Value)
                {
                    var bytes = new byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
                    yield return new IPAddress(bytes);
                }

                if (value == uint.MaxValue)
                {
                    yield break;
                }
            }
        }
    }
}
