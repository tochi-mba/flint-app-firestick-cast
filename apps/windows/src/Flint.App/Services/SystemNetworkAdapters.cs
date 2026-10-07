using System.Net.NetworkInformation;
using Flint.Core;

namespace Flint.App.Services;

/// <summary>This PC's network adapters, as <see cref="VpnDetection"/> reads them.</summary>
public static class SystemNetworkAdapters
{
    /// <summary>Every adapter Windows reports, loopback aside.</summary>
    public static IReadOnlyList<NetworkAdapterFacts> Read() =>
        [
            .. NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
                .Select(adapter => new NetworkAdapterFacts(
                    adapter.Name,
                    adapter.Description,
                    adapter.OperationalStatus is OperationalStatus.Up,
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Ppp)),
        ];

    /// <summary>The VPN on this PC now, or null when none is.</summary>
    public static string? ActiveVpn() => VpnDetection.ActiveVpn(Read());
}
