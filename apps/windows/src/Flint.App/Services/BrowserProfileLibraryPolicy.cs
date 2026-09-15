using System.Globalization;
using System.Net;
using System.Text;

namespace Flint.App.Services;

/// <summary>Normalization shared by loaded data and live mutations.</summary>
internal static class BrowserProfileLibraryPolicy
{
    private const int HttpsPort = 443;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = true };
    private static readonly string[] LocalSuffixes =
        [".localhost", ".local", ".lan", ".internal", ".home", ".localdomain"];

    public static BrowserProfileLibraryEntry? CreateEntry(
        string? url,
        string? title,
        long faviconId,
        DateTimeOffset? visitedAt)
    {
        var canonical = CanonicalizeUrl(url);
        if (canonical is null)
        {
            return null;
        }

        var normalizedTitle = NormalizeTitle(title ?? string.Empty);
        if (normalizedTitle.Length == 0)
        {
            normalizedTitle = TruncateUtf8(canonical, BrowserProfileLibraryLimits.MaxTitleBytes);
        }

        var timestamp = (visitedAt ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        return new BrowserProfileLibraryEntry(
            canonical,
            normalizedTitle,
            Math.Max(0, faviconId),
            Math.Max(0, timestamp));
    }

    public static string? CanonicalizeUrl(string? rawUrl)
    {
        if (rawUrl is null || HasUnpairedSurrogate(rawUrl) || rawUrl.Any(char.IsControl))
        {
            return null;
        }

        int inputBytes;
        try
        {
            inputBytes = StrictUtf8.GetByteCount(rawUrl);
        }
        catch (EncoderFallbackException)
        {
            return null;
        }

        if (inputBytes > BrowserProfileLibraryLimits.MaxUrlBytes)
        {
            return null;
        }

        var input = rawUrl.Trim();
        if (input.Length == 0 || input.Any(char.IsWhiteSpace) ||
            !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var authorityEnd = input.IndexOfAny(['/', '?', '#'], "https://".Length);
        if (authorityEnd < 0)
        {
            authorityEnd = input.Length;
        }

        var rawAuthority = input["https://".Length..authorityEnd];
        if (rawAuthority.Length == 0 || rawAuthority.Contains('@') || rawAuthority.Contains('\\') ||
            rawAuthority.StartsWith('['))
        {
            return null;
        }

        var separator = rawAuthority.LastIndexOf(':');
        if (separator >= 0 && rawAuthority.IndexOf(':') != separator)
        {
            return null;
        }

        var rawHost = separator < 0 ? rawAuthority : rawAuthority[..separator];
        var port = HttpsPort;
        if (separator >= 0 &&
            (!int.TryParse(rawAuthority[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
             port is < 1 or > 65_535))
        {
            return null;
        }

        if (rawHost.Length == 0 || rawHost.Contains('%') || rawHost.StartsWith('.') || rawHost.EndsWith('.'))
        {
            return null;
        }

        string host;
        try
        {
            // Uri/IdnMapping retain some non-ASCII casing under invariant globalization. The
            // receiver lower-cases its IDN host, so case-fold before punycode as well as after it.
            host = Idn.GetAscii(rawHost.ToLowerInvariant()).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (host.Length == 0 || host == "localhost" || !host.Contains('.') ||
            host.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            host.All(character => character is >= '0' and <= '9' || character == '.') ||
            LocalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.Ordinal)) ||
            host == "appassets.androidplatform.net" ||
            IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var asciiInput = $"https://{host}{(separator < 0 ? string.Empty : $":{port}")}{input[authorityEnd..]}";
        if (!Uri.TryCreate(asciiInput, UriKind.Absolute, out var uri) ||
            !StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, Uri.UriSchemeHttps) ||
            uri.UserInfo.Length != 0 ||
            !StringComparer.OrdinalIgnoreCase.Equals(uri.IdnHost, host))
        {
            return null;
        }

        var path = uri.AbsolutePath.Length == 0 ? "/" : uri.AbsolutePath;
        if (!path.StartsWith('/'))
        {
            return null;
        }

        var canonical = $"https://{host}{(port == HttpsPort ? string.Empty : $":{port}")}{path}{uri.Query}{uri.Fragment}";
        try
        {
            return StrictUtf8.GetByteCount(canonical) <= BrowserProfileLibraryLimits.MaxUrlBytes
                ? canonical
                : null;
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }

    public static string NormalizeTitle(string raw)
    {
        var normalized = new StringBuilder(Math.Min(raw.Length, BrowserProfileLibraryLimits.MaxTitleBytes));
        var pendingSpace = false;
        var offset = 0;
        while (offset < raw.Length)
        {
            var status = Rune.DecodeFromUtf16(raw.AsSpan(offset), out var rune, out var consumed);
            if (status != System.Buffers.OperationStatus.Done)
            {
                rune = Rune.ReplacementChar;
                consumed = 1;
                if (pendingSpace)
                {
                    normalized.Append(' ');
                }
                normalized.Append(rune.ToString());
                pendingSpace = true;
                offset += consumed;
                continue;
            }
            offset += consumed;

            if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune))
            {
                pendingSpace = normalized.Length != 0;
                continue;
            }

            if (pendingSpace)
            {
                normalized.Append(' ');
                pendingSpace = false;
            }
            normalized.Append(rune.ToString());
        }

        return TruncateUtf8(normalized.ToString(), BrowserProfileLibraryLimits.MaxTitleBytes);
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        if (StrictUtf8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }

        var result = new StringBuilder(Math.Min(value.Length, maximumBytes));
        var usedBytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (usedBytes + rune.Utf8SequenceLength > maximumBytes)
            {
                break;
            }

            result.Append(rune.ToString());
            usedBytes += rune.Utf8SequenceLength;
        }

        return result.ToString();
    }

    private static bool HasUnpairedSurrogate(string value)
    {
        var index = 0;
        while (index < value.Length)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 == value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return true;
                }
                index += 2;
            }
            else if (char.IsLowSurrogate(value[index]))
            {
                return true;
            }
            else
            {
                index++;
            }
        }

        return false;
    }
}
