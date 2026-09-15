using System.Net;
using System.Net.Sockets;

namespace Flint.Discovery.Browser;

/// <summary>
/// Immutable, non-secret routing metadata for a receiver's dedicated browser TLS listener.
/// </summary>
/// <remarks>
/// This model intentionally contains only an IPv4 receiver address, listener port, and browser
/// wire version. It is not receiver identity: a caller must still authenticate the TLS certificate
/// and apply the browser trust policy before sending any browser protocol data.
/// </remarks>
public sealed record BrowserEndpointAdvertisement
{
    /// <summary>The first browser protocol version that may be advertised.</summary>
    public const int MinimumSupportedProtocolVersion = 2;

    /// <summary>The largest version that fits the unsigned 16-bit wire envelope.</summary>
    public const int MaximumProtocolVersion = ushort.MaxValue;

    /// <summary>
    /// Creates routing metadata that has already passed the same invariants as the TXT parser.
    /// </summary>
    /// <param name="receiverAddress">The advertised receiver's unicast IPv4 address.</param>
    /// <param name="port">The dedicated browser TLS listener port.</param>
    /// <param name="protocolVersion">The highest browser protocol version advertised.</param>
    public BrowserEndpointAdvertisement(IPAddress receiverAddress, int port, int protocolVersion)
    {
        ArgumentNullException.ThrowIfNull(receiverAddress);
        if (!IsUsableUnicastIpv4(receiverAddress))
        {
            throw new ArgumentException("A browser endpoint requires a usable unicast IPv4 address.", nameof(receiverAddress));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(protocolVersion, MinimumSupportedProtocolVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(protocolVersion, MaximumProtocolVersion);

        // Preserve only the address value; discovery objects must not retain an arbitrary caller
        // object graph alongside the narrowly scoped routing facts.
        ReceiverAddress = new IPAddress(receiverAddress.GetAddressBytes());
        Port = port;
        ProtocolVersion = protocolVersion;
    }

    /// <summary>The receiver endpoint's advertised unicast IPv4 address.</summary>
    public IPAddress ReceiverAddress { get; }

    /// <summary>The dedicated browser TLS listener port.</summary>
    public int Port { get; }

    /// <summary>The highest browser protocol version advertised by the receiver.</summary>
    public int ProtocolVersion { get; }

    internal static bool IsUsableUnicastIpv4(IPAddress? address)
    {
        if (address is null || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        // 0/8 is unspecified, 127/8 is loopback, and 224/4 is multicast or reserved. Do not
        // constrain this further: private, CGNAT, link-local, and publicly addressed LANs are all
        // valid network environments when the user has explicitly selected the receiver.
        var octets = address.GetAddressBytes();
        return octets[0] is >= 1 and <= 223 && octets[0] != 127;
    }
}
