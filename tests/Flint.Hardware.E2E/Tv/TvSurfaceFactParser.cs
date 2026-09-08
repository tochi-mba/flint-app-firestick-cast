using System.Globalization;
using System.Text.RegularExpressions;

namespace Flint.Hardware.E2E;

/// <summary>
/// Parses a UiAutomator window dump (and optional logcat lines) into pairing facts without
/// trusting any secret material beyond what the TV already shows on screen.
/// </summary>
public static partial class TvSurfaceFactParser
{
    [GeneratedRegex(@"content-desc=""Pairing code ([0-9 ]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex PairingCodeDesc();

    [GeneratedRegex(@"text=""(\d{1,3}(?:\.\d{1,3}){3}):(\d{1,5})""", RegexOptions.CultureInvariant)]
    private static partial Regex EndpointText();

    [GeneratedRegex(@"content-desc=""Browser port (\d{1,5})""", RegexOptions.CultureInvariant)]
    private static partial Regex BrowserPortDesc();

    [GeneratedRegex(@"text=""([0-9A-F]{4}(?:-[0-9A-F]{4}){2,3})""", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintText();

    [GeneratedRegex(
        @"Browser TLS listening on \d{1,3}(?:\.\d{1,3}){3}:(\d{1,5})",
        RegexOptions.CultureInvariant)]
    private static partial Regex BrowserTlsLog();

    /// <summary>Builds facts from a uiautomator dump, optionally filled from logcat.</summary>
    public static TvSurfaceFacts Parse(string uiDumpXml, string? logcat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uiDumpXml);

        var pairing = PairingCodeDesc().Match(uiDumpXml);
        if (!pairing.Success)
        {
            throw new InvalidOperationException(
                "The TV surface dump does not contain a Pairing code content description. "
                + "Open Flint Receiver so the READY pairing panel is visible.");
        }

        var code = pairing.Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (code.Length != 6 || !code.All(char.IsDigit))
        {
            throw new InvalidOperationException($"Parsed pairing code '{code}' is not six digits.");
        }

        var endpoint = EndpointText().Match(uiDumpXml);
        if (!endpoint.Success)
        {
            throw new InvalidOperationException(
                "The TV surface dump does not contain an IP:port endpoint label.");
        }

        var address = endpoint.Groups[1].Value;
        var receiverPort = int.Parse(endpoint.Groups[2].Value, CultureInfo.InvariantCulture);

        int? browserPort = null;
        var browserMatch = BrowserPortDesc().Match(uiDumpXml);
        if (browserMatch.Success)
        {
            browserPort = int.Parse(browserMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        else if (!string.IsNullOrWhiteSpace(logcat))
        {
            var logMatch = BrowserTlsLog().Matches(logcat).LastOrDefault();
            if (logMatch is { Success: true })
            {
                browserPort = int.Parse(logMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        string? fingerprint = null;
        var fingerprintMatch = FingerprintText().Match(uiDumpXml);
        if (fingerprintMatch.Success)
        {
            fingerprint = fingerprintMatch.Groups[1].Value;
        }

        return new TvSurfaceFacts(code, address, receiverPort, browserPort, fingerprint);
    }
}
