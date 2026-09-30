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
    internal const int MaximumHosts = 512;
    private const int Concurrency = 16;
    private const int MaximumResponseBytes = 512;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(400);
    private static readonly byte[] Request = Encoding.ASCII.GetBytes("REXCAST DISCOVER/1\n");

    internal sealed record Answer(IPAddress Address, string ModelName, int ServicePort);

    /// <summary>One address on one adapter, as Windows reports it.</summary>
    internal sealed record AdapterAddress(
        OperationalStatus Status,
        NetworkInterfaceType Type,
        IPAddress Address,
        int PrefixLength);

    /// <summary>Probes every small private subnet this PC is on.</summary>
    internal static Task<IReadOnlyList<Answer>> ScanAsync(CancellationToken cancellationToken) =>
        ScanAsync(LocalSubnets(), Port, Timeout, cancellationToken);

    /// <summary>
    /// Probes <paramref name="subnets"/> on <paramref name="port"/>, each connection bounded by
    /// <paramref name="timeout"/>. The seam tests use to aim the real scan at a loopback receiver.
    /// </summary>
    internal static async Task<IReadOnlyList<Answer>> ScanAsync(
        IEnumerable<LocalSubnet> subnets,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var scans = subnets.Select(subnet => ScanAsync(subnet, port, timeout, cancellationToken));
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
            || !int.TryParse(fields[2], out var servicePort)
            || servicePort is < 1 or > 65_535)
        {
            return null;
        }

        return new Answer(address, fields[1], servicePort);
    }

    /// <summary>Every small private IPv4 subnet on this PC's adapters that a television could share.</summary>
    internal static IEnumerable<LocalSubnet> LocalSubnets() =>
        LocalSubnets(NetworkInterface.GetAllNetworkInterfaces().SelectMany(static nic =>
            nic.GetIPProperties().UnicastAddresses.Select(unicast => new AdapterAddress(
                nic.OperationalStatus,
                nic.NetworkInterfaceType,
                unicast.Address,
                unicast.PrefixLength))));

    /// <summary>
    /// The subnets worth sweeping among <paramref name="addresses"/>: IPv4, private, small, on an
    /// adapter that is up and is a link a television could be on.
    /// </summary>
    internal static IEnumerable<LocalSubnet> LocalSubnets(IEnumerable<AdapterAddress> addresses) =>
        addresses
            .Where(static adapter => adapter.Status == OperationalStatus.Up && CouldShareATelevision(adapter.Type))
            .Where(static adapter => adapter.Address.AddressFamily == AddressFamily.InterNetwork)
            .Where(static adapter => IsPrivate(adapter.Address))
            .Select(static adapter => new LocalSubnet(adapter.Address, adapter.PrefixLength))
            .Where(IsSweepable)
            .Distinct();

    /// <summary>
    /// Whether an adapter of this type can be on the same network as a television: Ethernet or Wi-Fi.
    /// </summary>
    /// <remarks>
    /// Named rather than excluded, because what else Windows reports is open-ended. Mobile broadband
    /// is the costly miss: a carrier often hands a USB modem or a tethered phone a small private
    /// subnet of its own, and probing the carrier's side finds no television and costs the person
    /// data. Dial-up, tunnels and virtual adapters such as WireGuard's, which reports a type .NET
    /// has no name for, are left out the same way.
    /// </remarks>
    private static bool CouldShareATelevision(NetworkInterfaceType type) => type is
        NetworkInterfaceType.Ethernet
        or NetworkInterfaceType.Ethernet3Megabit
        or NetworkInterfaceType.FastEthernetT
        or NetworkInterfaceType.FastEthernetFx
        or NetworkInterfaceType.GigabitEthernet
        or NetworkInterfaceType.Wireless80211;

    /// <summary>
    /// Whether a subnet is small enough to probe one host at a time, and has anyone else on it.
    /// </summary>
    /// <remarks>
    /// A /16 is sixty thousand connection attempts against machines that never asked to be
    /// probed, so anything beyond <see cref="MaximumHosts"/> is left to advertisement and to a
    /// typed-in address.
    /// </remarks>
    internal static bool IsSweepable(LocalSubnet subnet) => subnet.HostCount is > 0 and <= MaximumHosts;

    /// <summary>Whether <paramref name="address"/> is in one of the three RFC 1918 private ranges.</summary>
    internal static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4
            && (bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
    }

    private static async Task<IReadOnlyList<Answer>> ScanAsync(
        LocalSubnet subnet,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Concurrency, Concurrency);
        var probes = subnet.Hosts().Select(async address =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await ProbeAsync(subnet.Address, address, port, timeout, cancellationToken).ConfigureAwait(false);
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
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(local, 0));
            await socket.ConnectAsync(new IPEndPoint(target, port), deadline.Token).ConfigureAwait(false);
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

    /// <summary>One local address and its prefix: the hosts a probe may contact.</summary>
    internal sealed record LocalSubnet(IPAddress Address, int PrefixLength)
    {
        private uint Value => BinaryPrimitives.ReadUInt32BigEndian(Address.GetAddressBytes());

        private uint Mask => PrefixLength == 0 ? 0 : uint.MaxValue << (32 - PrefixLength);

        // /31 and /32 have no network or broadcast address to leave out (RFC 3021).
        private uint First => (Value & Mask) + (PrefixLength <= 30 ? 1u : 0u);

        private uint Last => (Value | ~Mask) - (PrefixLength <= 30 ? 1u : 0u);

        /// <summary>How many addresses <see cref="Hosts"/> yields: the range, less this PC's own address.</summary>
        /// <remarks>
        /// The range is never empty: a /31 or /32 keeps both ends, and every wider prefix has at
        /// least two hosts between its network and broadcast addresses.
        /// </remarks>
        internal long HostCount => (long)Last - First + 1 - (Value >= First && Value <= Last ? 1 : 0);

        /// <summary>Every other host on the subnet, in address order.</summary>
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
