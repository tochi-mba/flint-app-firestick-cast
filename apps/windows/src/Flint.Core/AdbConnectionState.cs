namespace Flint.Core;

/// <summary>
/// The result of attempting an ADB connection to a discovered device.
/// </summary>
/// <remarks>
/// This is the only evidence Flint has for whether a device is Android-based. A refusal is
/// genuinely ambiguous — see <see cref="Refused"/> — and must not be reported as a Vega device.
/// </remarks>
public enum AdbConnectionState
{
    /// <summary>No attempt has been made yet.</summary>
    NotProbed = 0,

    /// <summary>
    /// Nothing accepted a connection across the scanned range. This does NOT prove the device runs
    /// Vega: ADB debugging may simply be switched off in Developer Options. The two cases are
    /// indistinguishable from the network and must be reported as ambiguous.
    /// </summary>
    Refused = 1,

    /// <summary>
    /// The device accepted the connection but has not authorised this host. The television is
    /// showing, or has dismissed, an RSA prompt. This proves the device is Android-based.
    /// </summary>
    Unauthorized = 2,

    /// <summary>Connected and authorised. Device properties can be read.</summary>
    Connected = 3,

    /// <summary>The connection attempt exceeded its deadline without a definite answer.</summary>
    TimedOut = 4,
}
