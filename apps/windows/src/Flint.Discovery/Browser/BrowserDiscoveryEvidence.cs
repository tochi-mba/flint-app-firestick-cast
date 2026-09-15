using Flint.Core;
using Flint.Discovery.Browser;

namespace Flint.Discovery;

/// <summary>
/// Maps non-secret mDNS browser TXT attributes into eligibility evidence without treating the
/// advertisement as a trust decision.
/// </summary>
public static class BrowserDiscoveryEvidence
{
    /// <summary>
    /// Builds browser evidence from one advertised instance, or null when the advertisement does
    /// not claim any browser metadata.
    /// </summary>
    public static BrowserReceiverEvidence? FromServiceInstance(ServiceInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        if (!instance.TryResolveBrowserEndpoint(instance.Address, out var endpoint, out var error))
        {
            // Conflicting/malformed browser attributes must not look like a healthy secure endpoint.
            if (error is BrowserEndpointAdvertisementError.None
                or BrowserEndpointAdvertisementError.MissingBrowserPort
                or BrowserEndpointAdvertisementError.MissingBrowserProtocol)
            {
                return null;
            }

            return new BrowserReceiverEvidence(
                MaximumProtocolVersion: 0,
                SecureEndpointAvailable: false,
                WebViewProbe: BrowserWebViewProbe.Inconclusive);
        }

        // A clean browser_port advertisement means the updated receiver with the dedicated TLS
        // listener is running. The physical WebView smoke script may still downgrade this to
        // Unsupported later; discovery never invents a Passed result after an explicit failure.
        return new BrowserReceiverEvidence(
            MaximumProtocolVersion: endpoint!.ProtocolVersion,
            SecureEndpointAvailable: true,
            WebViewProbe: BrowserWebViewProbe.Passed)
        {
            SecureEndpointPort = endpoint.Port,
        };
    }
}
