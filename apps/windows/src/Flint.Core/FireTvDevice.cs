using System.Net;

namespace Flint.Core;

/// <summary>
/// A Fire TV device as Flint currently understands it.
/// </summary>
/// <remarks>
/// Every field is evidence, not inference. <see cref="Platform"/> stays
/// <see cref="FireTvPlatform.Unknown"/> until something actually proved it.
/// </remarks>
/// <param name="Address">The device's address on the local link.</param>
/// <param name="FriendlyName">The name the device advertises, or the address when it advertises none.</param>
/// <param name="Source">How this device was found.</param>
public sealed record FireTvDevice(
    IPAddress Address,
    string FriendlyName,
    DiscoverySource Source)
{
    /// <summary>The ADB port that answered, when one did.</summary>
    public int? AdbPort { get; init; }

    /// <summary>The outcome of the ADB probe.</summary>
    public AdbConnectionState AdbState { get; init; } = AdbConnectionState.NotProbed;

    /// <summary>Determined platform family, or <see cref="FireTvPlatform.Unknown"/>.</summary>
    public FireTvPlatform Platform { get; init; } = FireTvPlatform.Unknown;

    /// <summary>Hardware model string, when the device reported one.</summary>
    public string? Model { get; init; }

    /// <summary>The underlying Android release string reported by the device.</summary>
    public string? AndroidRelease { get; init; }

    /// <summary>
    /// Android API level, read independently of <see cref="AndroidRelease"/>.
    /// </summary>
    public int? AndroidApiLevel { get; init; }

    /// <summary>
    /// Non-secret evidence for the optional TV-resident browser experiment.
    /// </summary>
    /// <remarks>
    /// A missing value is intentionally not interpreted as support. Certificate identity, pairing
    /// material, URLs, page state, and browser data do not belong to discovery evidence.
    /// </remarks>
    public BrowserReceiverEvidence? BrowserEvidence { get; init; }

    /// <summary>Whether the device is reachable enough to attempt a session.</summary>
    public bool IsReachable => AdbState is AdbConnectionState.Connected or AdbConnectionState.Unauthorized;
}
