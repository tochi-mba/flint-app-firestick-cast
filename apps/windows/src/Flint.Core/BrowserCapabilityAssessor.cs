namespace Flint.Core;

/// <summary>
/// Reduces only receiver evidence into a conservative browser eligibility verdict.
/// </summary>
/// <remarks>
/// This is deliberately pure. It neither probes ADB nor opens a socket, so the same observed
/// facts always produce the same UI and diagnostics result. Later TLS trust is a separate runtime
/// decision and cannot be inferred here.
/// </remarks>
public static class BrowserCapabilityAssessor
{
    /// <summary>The first protocol version that can carry browser traffic over the separate TLS route.</summary>
    public const int MinimumBrowserProtocolVersion = 2;

    /// <summary>Returns one exact browser verdict for the selected receiver.</summary>
    public static BrowserCapability Assess(FireTvDevice? receiver)
    {
        if (receiver is null)
        {
            return BrowserCapability.From(BrowserAvailability.UnsupportedPlatform);
        }

        if (receiver.Platform == FireTvPlatform.Vega)
        {
            return new BrowserCapability(
                BrowserAvailability.UnsupportedPlatform,
                "This device runs Vega OS, which cannot install the Android receiver Flint's browser experiment requires.",
                Remedy: null);
        }

        // Unknown platform usually means ADB never identified the OS. A clean secure-browser
        // advertisement still proves our Android receiver is running — that TXT only exists there —
        // so eligibility may continue from evidence rather than waiting on ADB.
        if (receiver.Platform == FireTvPlatform.Unknown
            && receiver.BrowserEvidence is not { SecureEndpointAvailable: true })
        {
            return BrowserCapability.From(BrowserAvailability.UnsupportedPlatform);
        }

        if (receiver.Platform is not FireTvPlatform.Unknown && !receiver.Platform.IsAndroidBased())
        {
            return BrowserCapability.From(BrowserAvailability.UnsupportedPlatform);
        }

        var evidence = receiver.BrowserEvidence;
        if (evidence is null)
        {
            return BrowserCapability.From(BrowserAvailability.WebViewSmokeTestPending);
        }

        if (evidence.MaximumProtocolVersion < MinimumBrowserProtocolVersion)
        {
            return BrowserCapability.From(BrowserAvailability.ProtocolTooOld);
        }

        if (!evidence.SecureEndpointAvailable)
        {
            return BrowserCapability.From(BrowserAvailability.SecureEndpointUnavailable);
        }

        return evidence.WebViewProbe switch
        {
            BrowserWebViewProbe.Passed => BrowserCapability.From(BrowserAvailability.Available),
            BrowserWebViewProbe.Unsupported => BrowserCapability.From(BrowserAvailability.WebViewUnsupported),
            _ => BrowserCapability.From(BrowserAvailability.WebViewSmokeTestPending),
        };
    }
}
