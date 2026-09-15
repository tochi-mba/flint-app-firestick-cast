using System.Net;
using System.Net.Sockets;

namespace Flint.Discovery;

/// <summary>
/// Sends one multicast query and collects whatever answers arrive.
/// </summary>
/// <remarks>
/// Shared by device discovery and the service-enumeration diagnostic, so both bind sockets the same
/// way. Every socket binds to a specific interface address rather than the wildcard: Flint must
/// never listen on every network the machine happens to be attached to.
/// </remarks>
public static class MulticastServiceScanner
{
    /// <summary>
    /// Queries every usable interface and hands each received datagram to <paramref name="handle"/>.
    /// </summary>
    /// <param name="serviceType">The service type to ask about.</param>
    /// <param name="window">How long to listen for answers.</param>
    /// <param name="handle">
    /// Called for each datagram received. Runs on the receive path, so it must be quick and must
    /// not throw for a packet it cannot parse.
    /// </param>
    public static async Task QueryAsync(
        string serviceType,
        TimeSpan window,
        Action<ReadOnlyMemory<byte>> handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceType);
        ArgumentNullException.ThrowIfNull(handle);

        var tasks = LocalIPv4Addresses()
            .Select(address => QueryInterfaceAsync(address, serviceType, window, handle, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task QueryInterfaceAsync(
        IPAddress local,
        string serviceType,
        TimeSpan window,
        Action<ReadOnlyMemory<byte>> handle,
        CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        try
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            socket.Bind(new IPEndPoint(local, 0));
            socket.SetSocketOption(
                SocketOptionLevel.IP,
                SocketOptionName.MulticastInterface,
                local.GetAddressBytes());

            var query = MulticastDnsCodec.BuildQuery(serviceType);
            var group = new IPEndPoint(MulticastDnsCodec.MulticastAddress, MulticastDnsCodec.MulticastPort);
            await socket.SendToAsync(query, group, cancellationToken).ConfigureAwait(false);

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(window);

            var buffer = new byte[8192];
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (!deadline.Token.IsCancellationRequested)
            {
                var received = await socket
                    .ReceiveFromAsync(buffer, SocketFlags.None, any, deadline.Token)
                    .ConfigureAwait(false);
                handle(buffer.AsMemory(0, received.ReceivedBytes));
            }
        }
        catch (OperationCanceledException)
        {
            // The listen window closed. Whatever answered in time is the result.
        }
        catch (SocketException)
        {
            // This interface cannot carry multicast. Others still can.
        }
    }

    /// <summary>
    /// Every usable local IPv4 address.
    /// </summary>
    /// <remarks>
    /// Derived rather than assumed. Nothing here hardcodes a subnet, a prefix length, or an
    /// interface name.
    /// </remarks>
    public static IEnumerable<IPAddress> LocalIPv4Addresses() =>
        System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
            .Where(nic => nic.NetworkInterfaceType
                != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
            .Where(nic => nic.SupportsMulticast)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .Distinct();
}
