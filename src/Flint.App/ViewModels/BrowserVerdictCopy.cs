using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>
/// The words the Web page uses for each state it can be in.
/// </summary>
/// <remarks>
/// Pulled out of the page so the copy sits together and can be read as a set. Three sentences that
/// have to agree — a status, an explanation, and a next step — are far easier to keep consistent
/// side by side than scattered through a view model that is also managing a TLS session.
///
/// Pure functions of the phase and the verdict. Nothing here reaches for live state, so the copy
/// can be reviewed and tested without standing up a session.
/// </remarks>
internal static class BrowserVerdictCopy
{
    /// <summary>The short word in the page's heading pill.</summary>
    internal static string StatusLabel(BrowserUiPhase phase, BrowserAvailability availability) => phase switch
    {
        BrowserUiPhase.SecureReady => "Secure ready",
        BrowserUiPhase.Verifying => "Verifying",
        BrowserUiPhase.Mismatch => "Mismatch",
        _ => availability switch
        {
            BrowserAvailability.Available => "Eligible",
            BrowserAvailability.ProtocolTooOld => "Update receiver",
            BrowserAvailability.SecureEndpointUnavailable => "Secure service missing",
            BrowserAvailability.WebViewSmokeTestPending => "Evidence needed",
            BrowserAvailability.WebViewUnsupported => "Unsupported",
            _ => "Unavailable",
        },
    };

    /// <summary>
    /// Why the page is in this state.
    /// </summary>
    /// <remarks>
    /// Falls through to the capability verdict's own reason rather than rephrasing it, so the page
    /// and diagnostics never give two different accounts of the same device.
    /// </remarks>
    internal static string Reason(BrowserUiPhase phase, BrowserCapability capability, string? lastError) =>
        phase switch
        {
            BrowserUiPhase.SecureReady =>
                "Secure receiver ready — enter an HTTPS address to open it on the TV.",
            BrowserUiPhase.Verifying =>
                "Comparing the TV security code and completing the pinned TLS session.",
            BrowserUiPhase.Mismatch => lastError ?? "The secure browser session could not be verified.",
            _ => capability.Reason,
        };

    /// <summary>
    /// A transport failure, bounded for display.
    /// </summary>
    /// <remarks>
    /// Exception text reaches the page and assistive UI, so it is length-capped like every other
    /// string here, and an empty message becomes a sentence rather than a blank red line.
    /// </remarks>
    internal static string SafeMessage(Exception exception)
    {
        var text = exception.Message;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "The secure browser session could not be verified.";
        }

        return text.Length <= 240 ? text : text[..240];
    }

    /// <summary>The next thing a person can actually do, or null when there is nothing to offer.</summary>
    internal static string? Remedy(BrowserUiPhase phase, BrowserCapability capability) => phase switch
    {
        BrowserUiPhase.SecureReady =>
            "Type a search or a site, then press Enter. Starting Web stops mirror or media on the TV first.",
        BrowserUiPhase.Mismatch =>
            "Refresh discovery, compare both screens, enter the Cast pairing code, and verify again.",
        // Nothing useful to say mid-handshake, and inventing something would be noise at the exact
        // moment the page is asking for patience.
        BrowserUiPhase.Verifying => null,
        _ => capability.Remedy,
    };
}
