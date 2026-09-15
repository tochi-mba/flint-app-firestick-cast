namespace Flint.Core;

/// <summary>A browser eligibility verdict with exact, user-facing safe copy.</summary>
/// <param name="Availability">The closed set of browser availability outcomes.</param>
/// <param name="Reason">A bounded explanation suitable for visible and assistive UI.</param>
/// <param name="Remedy">An actionable next step, when there is one.</param>
public sealed record BrowserCapability(
    BrowserAvailability Availability,
    string Reason,
    string? Remedy)
{
    /// <summary>Whether the receiver is eligible to begin secure browser verification.</summary>
    public bool IsEligible => Availability == BrowserAvailability.Available;

    /// <summary>The neutral default used by reports constructed outside the probe pipeline.</summary>
    public static BrowserCapability Unverified => From(BrowserAvailability.UnsupportedPlatform);

    /// <summary>Creates the canonical presentation for one availability outcome.</summary>
    public static BrowserCapability From(BrowserAvailability availability)
    {
        var copy = BrowserCapabilityCopy.For(availability);
        return new BrowserCapability(availability, copy.Reason, copy.Remedy);
    }
}

/// <summary>Centralized bounded copy for browser availability; UI and diagnostics share it verbatim.</summary>
public sealed record BrowserCapabilityCopy(string Reason, string? Remedy)
{
    /// <summary>Maximum length of any availability string to keep diagnostics and assistive UI bounded.</summary>
    public const int MaximumCopyLength = 360;

    /// <summary>Returns the one safe explanation for <paramref name="availability"/>.</summary>
    public static BrowserCapabilityCopy For(BrowserAvailability availability) => availability switch
    {
        BrowserAvailability.UnsupportedPlatform => new(
            "Flint has not verified this receiver as an Android-based Fire OS device that can run its browser experiment.",
            "Open Flint Receiver on the TV, enable ADB if you can, or type the browser port shown on the TV into Web and verify."),
        BrowserAvailability.ProtocolTooOld => new(
            "This receiver does not support the TLS-only browser protocol required by Flint.",
            "Update the selected Flint receiver, then verify it again."),
        BrowserAvailability.SecureEndpointUnavailable => new(
            "This receiver has not advertised the dedicated secure browser endpoint.",
            "Open the updated receiver on the TV and verify its secure browser service."),
        BrowserAvailability.WebViewSmokeTestPending => new(
            "The receiver's local WebView compatibility check has not completed, so browser controls stay unavailable.",
            "Run the explicit receiver compatibility probe and review its result."),
        BrowserAvailability.WebViewUnsupported => new(
            "The receiver's local WebView did not meet Flint's controlled browser compatibility requirement.",
            null),
        BrowserAvailability.Available => new(
            "This receiver is eligible for the secure TV-resident browser. Eligibility is not evidence that any site works.",
            "Verify the receiver's secure browser session before navigating."),
        _ => For(BrowserAvailability.UnsupportedPlatform),
    };
}
