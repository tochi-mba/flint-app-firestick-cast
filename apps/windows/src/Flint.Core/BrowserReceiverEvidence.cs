namespace Flint.Core;

/// <summary>
/// Non-secret browser eligibility facts advertised or recorded for one receiver.
/// </summary>
/// <remarks>
/// This value intentionally contains no URL, pairing code, certificate, pin, cookie, page title,
/// or preview data. Endpoint discovery is routing metadata only; identity is established later by
/// the pinned TLS session.
/// </remarks>
/// <param name="MaximumProtocolVersion">Highest browser protocol version the receiver advertises.</param>
/// <param name="SecureEndpointAvailable">Whether a dedicated TLS browser endpoint is advertised.</param>
/// <param name="WebViewProbe">The receiver-local controlled WebView probe outcome.</param>
public sealed record BrowserReceiverEvidence(
    int MaximumProtocolVersion,
    bool SecureEndpointAvailable,
    BrowserWebViewProbe WebViewProbe)
{
    /// <summary>
    /// Dedicated TLS browser listener port from non-secret discovery metadata, when advertised.
    /// </summary>
    /// <remarks>
    /// Routing only. Identity is established later by the pinned TLS session; a missing port keeps
    /// eligibility false via <see cref="SecureEndpointAvailable"/> rather than inventing a default.
    /// </remarks>
    public int? SecureEndpointPort { get; init; }
}
