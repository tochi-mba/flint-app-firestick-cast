namespace Flint.Core;

/// <summary>One network adapter, as far as spotting a VPN goes.</summary>
/// <param name="Name">What Windows calls the connection, such as "ProtonVPN" or "WiFi".</param>
/// <param name="Description">The adapter's driver description.</param>
/// <param name="IsUp">Whether it is connected.</param>
/// <param name="IsPointToPoint">
/// Whether Windows reports it as a point-to-point link, which is how its own VPN connections appear.
/// Not "tunnel": Windows' IPv6 transition adapters, such as Teredo, are tunnels and are often up.
/// </param>
public sealed record NetworkAdapterFacts(string Name, string Description, bool IsUp, bool IsPointToPoint);

/// <summary>
/// Whether a VPN is on, and what to say about it when the TV cannot be reached.
/// </summary>
/// <remarks>
/// A VPN on this PC can carry local traffic into its tunnel, where the TV cannot answer: the TV
/// then seems to be off, or ignores pairing. Seen with ProtonVPN, where the TV answered a scan and
/// then stopped answering ADB until the VPN was turned off. Flint does not touch the VPN; it says
/// which one is on and what to change.
/// </remarks>
public static class VpnDetection
{
    /// <summary>Words in an adapter's name or driver that mark it as a VPN.</summary>
    private static readonly string[] Signs =
    [
        "vpn", "wireguard", "wintun", "openvpn", "tap-windows", "nordlynx", "proton", "mullvad",
        "surfshark", "anyconnect", "globalprotect", "fortinet", "zscaler", "pangp",
    ];

    /// <summary>The name of the first VPN that is up, or null when none is.</summary>
    public static string? ActiveVpn(IEnumerable<NetworkAdapterFacts> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        return adapters
            .Where(adapter => adapter.IsUp && (adapter.IsPointToPoint || LooksLikeAVpn(adapter)))
            .Select(adapter => adapter.Name)
            .FirstOrDefault();
    }

    /// <summary>What to tell someone whose TV cannot be reached while <paramref name="vpn"/> is on.</summary>
    public static string Advice(string vpn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vpn);
        return $"{vpn} is on. A VPN can send this PC's local traffic into its tunnel, where the TV cannot "
            + $"answer. Allow local network access in {vpn}'s settings, or turn it off while you cast.";
    }

    private static bool LooksLikeAVpn(NetworkAdapterFacts adapter) =>
        Signs.Any(sign => adapter.Name.Contains(sign, StringComparison.OrdinalIgnoreCase)
            || adapter.Description.Contains(sign, StringComparison.OrdinalIgnoreCase));
}
