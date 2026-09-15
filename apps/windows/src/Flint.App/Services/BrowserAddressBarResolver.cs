using System.Text.RegularExpressions;

namespace Flint.App.Services;

/// <summary>
/// Turns address-bar text into an https URL the TV WebView can open.
/// </summary>
/// <remarks>
/// Search queries become a Google results page. Host-like text gets an https:// prefix.
/// The receiver only accepts https, so http:// is rewritten rather than rejected here.
/// </remarks>
public static partial class BrowserAddressBarResolver
{
    private const string GoogleSearchPrefix = "https://www.google.com/search?q=";

    /// <summary>
    /// Resolves typed address-bar text to a navigation URL, or <see langword="null"/> when empty.
    /// </summary>
    public static string? TryResolve(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var input = raw.Trim();
        if (LooksLikeSearchQuery(input))
        {
            return ToGoogleSearch(input);
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return absolute.AbsoluteUri;
            }

            if (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                var builder = new UriBuilder(absolute) { Scheme = Uri.UriSchemeHttps, Port = -1 };
                return builder.Uri.AbsoluteUri;
            }

            // javascript:, file:, etc. — safer as a search than as a rejected scheme on the TV.
            return ToGoogleSearch(input);
        }

        if (LooksLikeHostOrPath(input))
        {
            var candidate = "https://" + input;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var hostUri)
                && hostUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(hostUri.Host))
            {
                return hostUri.AbsoluteUri;
            }
        }

        return ToGoogleSearch(input);
    }

    private static bool LooksLikeSearchQuery(string input) =>
        input.Contains(' ', StringComparison.Ordinal)
        || input.Contains('\t', StringComparison.Ordinal);

    private static bool LooksLikeHostOrPath(string input)
    {
        // Single tokens without a dot are almost always search ("weather", "maps"), not hosts.
        // A slash implies path intent ("example.com/a" or rarely "/path" — latter still searches if no host).
        if (!input.Contains('.', StringComparison.Ordinal) && !input.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        return HostOrPathPattern().IsMatch(input);
    }

    private static string ToGoogleSearch(string query) =>
        GoogleSearchPrefix + Uri.EscapeDataString(query);

    [GeneratedRegex(
        @"^[A-Za-z0-9]([A-Za-z0-9\-._~:/?#\[\]@!$&'()*+,;=%]*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HostOrPathPattern();
}
