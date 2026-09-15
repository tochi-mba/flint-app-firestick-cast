using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Flint.Core;

/// <summary>A validated receiver endpoint entered by the user.</summary>
/// <param name="Address">One unicast IP address.</param>
/// <param name="Port">One explicit ADB port, or null to use the documented bounded range.</param>
public sealed record ManualProbeEndpoint(IPAddress Address, int? Port)
{
    /// <summary>Parses fields from the desktop or CLI without ever accepting a wildcard target.</summary>
    public static bool TryParse(
        string? addressText,
        string? portText,
        out ManualProbeEndpoint? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;

        if (!IPAddress.TryParse(addressText?.Trim(), out var address)
            || IsUnusableTarget(address))
        {
            error = "Enter a valid unicast IP address from the Fire TV's Network screen.";
            return false;
        }

        int? port = null;
        if (!string.IsNullOrWhiteSpace(portText))
        {
            if (!int.TryParse(
                    portText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var parsedPort)
                || parsedPort is < 1 or > 65535)
            {
                error = "Enter a port from 1 to 65535, or leave it blank to scan 5555–5585.";
                return false;
            }

            port = parsedPort;
        }

        endpoint = new ManualProbeEndpoint(address, port);
        return true;
    }

    private static bool IsUnusableTarget(IPAddress address)
    {
        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast)
            || address.IsIPv6Multicast)
        {
            return true;
        }

        if (address.AddressFamily is AddressFamily.InterNetwork)
        {
            var firstOctet = address.GetAddressBytes()[0];
            return firstOctet is >= 224 and <= 239;
        }

        return false;
    }
}
