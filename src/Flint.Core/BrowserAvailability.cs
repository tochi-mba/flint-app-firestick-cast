namespace Flint.Core;

/// <summary>
/// The truthful state of the experimental TV-resident browser on one selected receiver.
/// </summary>
/// <remarks>
/// These values intentionally do not reuse <see cref="ModeStatus"/>. Browser readiness has
/// security and platform gates that are not cast-mode outcomes, and collapsing them into a generic
/// status would make the user-facing explanation vague or optimistic.
/// </remarks>
public enum BrowserAvailability
{
    /// <summary>The selected device is not a verified Android-based Fire OS receiver.</summary>
    UnsupportedPlatform = 0,

    /// <summary>The receiver cannot speak the TLS-only browser protocol version.</summary>
    ProtocolTooOld = 1,

    /// <summary>The receiver did not advertise its dedicated secure browser endpoint.</summary>
    SecureEndpointUnavailable = 2,

    /// <summary>The receiver has not yet produced a conclusive local WebView compatibility result.</summary>
    WebViewSmokeTestPending = 3,

    /// <summary>The receiver's local WebView probe found a blocking incompatibility.</summary>
    WebViewUnsupported = 4,

    /// <summary>All local eligibility evidence passed; this is not a claim that any site loaded.</summary>
    Available = 5,
}
