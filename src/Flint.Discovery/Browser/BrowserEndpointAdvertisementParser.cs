using System.Globalization;
using System.Net;
using System.Text;

namespace Flint.Discovery.Browser;

/// <summary>
/// Parses untrusted DNS-SD TXT metadata into a browser TLS endpoint route.
/// </summary>
/// <remarks>
/// The parser is intentionally not a trust mechanism. It validates only a bounded port/version
/// route for the receiver the user already selected. Certificate pinning and browser authentication
/// remain separate, mandatory stages.
/// </remarks>
public static class BrowserEndpointAdvertisementParser
{
    /// <summary>The TXT key containing the dedicated browser TLS port.</summary>
    public const string BrowserPortKey = "browser_port";

    /// <summary>The TXT key containing the highest supported browser protocol version.</summary>
    public const string BrowserProtocolKey = "browser_protocol";

    /// <summary>The maximum number of TXT attributes inspected from one advertisement.</summary>
    public const int MaximumTextAttributeCount = 32;

    /// <summary>The maximum UTF-8 byte length of one DNS-SD TXT string.</summary>
    public const int MaximumTextAttributeBytes = 255;

    /// <summary>The maximum UTF-8 byte length of a browser port or version value.</summary>
    public const int MaximumBrowserValueBytes = 16;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private static readonly HashSet<string> SensitiveBrowserKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "browser_pin",
        "browser_fingerprint",
        "browser_certificate",
        "browser_cert",
        "browser_spki",
        "browser_pairing",
        "browser_pairing_code",
        "browser_token",
        "browser_session_token",
    };

    /// <summary>
    /// Parses a raw, ordered collection of TXT strings.
    /// </summary>
    /// <param name="sourceAddress">The IPv4 address that the service records resolved for the receiver.</param>
    /// <param name="textAttributes">Every TXT string, without dictionary normalization.</param>
    /// <param name="endpoint">The untrusted routing result when parsing succeeds.</param>
    /// <param name="error">The safe, non-secret reason parsing failed.</param>
    /// <returns><see langword="true"/> only for a complete, internally consistent route.</returns>
    public static bool TryParse(
        IPAddress? sourceAddress,
        IEnumerable<string> textAttributes,
        out BrowserEndpointAdvertisement? endpoint,
        out BrowserEndpointAdvertisementError error)
    {
        ArgumentNullException.ThrowIfNull(textAttributes);

        if (sourceAddress is null || !BrowserEndpointAdvertisement.IsUsableUnicastIpv4(sourceAddress))
        {
            return Fail(BrowserEndpointAdvertisementError.InvalidSourceAddress, out endpoint, out error);
        }

        var values = new BrowserAttributeValues();
        var count = 0;
        foreach (var textAttribute in textAttributes)
        {
            if (++count > MaximumTextAttributeCount)
            {
                return Fail(BrowserEndpointAdvertisementError.TooManyTextAttributes, out endpoint, out error);
            }

            if (!TryGetUtf8ByteCount(textAttribute, out var attributeByteCount) || string.IsNullOrEmpty(textAttribute))
            {
                return Fail(BrowserEndpointAdvertisementError.MalformedTextAttribute, out endpoint, out error);
            }

            if (attributeByteCount > MaximumTextAttributeBytes)
            {
                return Fail(BrowserEndpointAdvertisementError.OversizedTextAttribute, out endpoint, out error);
            }

            var separator = textAttribute.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                if (IsBrowserKey(textAttribute))
                {
                    return Fail(BrowserEndpointAdvertisementError.MalformedBrowserAttribute, out endpoint, out error);
                }

                if (IsSensitiveBrowserKey(textAttribute))
                {
                    return Fail(BrowserEndpointAdvertisementError.SensitiveBrowserAttribute, out endpoint, out error);
                }

                // DNS-SD permits bare non-browser flags. They are irrelevant to this endpoint.
                continue;
            }

            var key = textAttribute[..separator];
            var value = textAttribute[(separator + 1)..];
            if (!TryAcceptAttribute(key, value, values, out error))
            {
                endpoint = null;
                return false;
            }
        }

        return TryBuildEndpoint(sourceAddress, values, out endpoint, out error);
    }

    /// <summary>
    /// Parses TXT attributes preserved as an ordered key-value sequence.
    /// </summary>
    /// <remarks>
    /// A dictionary cannot satisfy this overload: it has already discarded duplicate keys. Supply
    /// an ordered list that retains each original TXT entry for strict validation.
    /// </remarks>
    /// <param name="sourceAddress">The IPv4 address that the service records resolved for the receiver.</param>
    /// <param name="textAttributes">Every TXT attribute in arrival order.</param>
    /// <param name="endpoint">The untrusted routing result when parsing succeeds.</param>
    /// <param name="error">The safe, non-secret reason parsing failed.</param>
    /// <returns><see langword="true"/> only for a complete, internally consistent route.</returns>
    public static bool TryParse(
        IPAddress? sourceAddress,
        IReadOnlyList<KeyValuePair<string, string>> textAttributes,
        out BrowserEndpointAdvertisement? endpoint,
        out BrowserEndpointAdvertisementError error)
    {
        ArgumentNullException.ThrowIfNull(textAttributes);

        if (sourceAddress is null || !BrowserEndpointAdvertisement.IsUsableUnicastIpv4(sourceAddress))
        {
            return Fail(BrowserEndpointAdvertisementError.InvalidSourceAddress, out endpoint, out error);
        }

        if (textAttributes.Count > MaximumTextAttributeCount)
        {
            return Fail(BrowserEndpointAdvertisementError.TooManyTextAttributes, out endpoint, out error);
        }

        var values = new BrowserAttributeValues();
        foreach (var textAttribute in textAttributes)
        {
            if (!TryGetUtf8ByteCount(textAttribute.Key, out var keyByteCount)
                || !TryGetUtf8ByteCount(textAttribute.Value, out var valueByteCount)
                || string.IsNullOrEmpty(textAttribute.Key))
            {
                return Fail(BrowserEndpointAdvertisementError.MalformedTextAttribute, out endpoint, out error);
            }

            if (keyByteCount + 1 + valueByteCount > MaximumTextAttributeBytes)
            {
                return Fail(BrowserEndpointAdvertisementError.OversizedTextAttribute, out endpoint, out error);
            }

            if (!TryAcceptAttribute(textAttribute.Key, textAttribute.Value, values, out error))
            {
                endpoint = null;
                return false;
            }
        }

        return TryBuildEndpoint(sourceAddress, values, out endpoint, out error);
    }

    /// <summary>
    /// Parses TXT strings only when their resolved source is the receiver already selected by the
    /// user. An advertisement for another local device is never reused as a browser route.
    /// </summary>
    public static bool TryResolve(
        IPAddress? selectedReceiverAddress,
        IPAddress? sourceAddress,
        IEnumerable<string> textAttributes,
        out BrowserEndpointAdvertisement? endpoint,
        out BrowserEndpointAdvertisementError error)
    {
        if (!TryValidateSelectedReceiver(selectedReceiverAddress, sourceAddress, out var validationError))
        {
            return Fail(validationError, out endpoint, out error);
        }

        return TryParse(sourceAddress, textAttributes, out endpoint, out error);
    }

    /// <summary>
    /// Resolves ordered key-value TXT attributes only for the user-selected receiver.
    /// </summary>
    public static bool TryResolve(
        IPAddress? selectedReceiverAddress,
        IPAddress? sourceAddress,
        IReadOnlyList<KeyValuePair<string, string>> textAttributes,
        out BrowserEndpointAdvertisement? endpoint,
        out BrowserEndpointAdvertisementError error)
    {
        if (!TryValidateSelectedReceiver(selectedReceiverAddress, sourceAddress, out var validationError))
        {
            return Fail(validationError, out endpoint, out error);
        }

        return TryParse(sourceAddress, textAttributes, out endpoint, out error);
    }

    private static bool TryBuildEndpoint(
        IPAddress sourceAddress,
        BrowserAttributeValues values,
        out BrowserEndpointAdvertisement? endpoint,
        out BrowserEndpointAdvertisementError error)
    {
        if (values.Port is null)
        {
            return Fail(BrowserEndpointAdvertisementError.MissingBrowserPort, out endpoint, out error);
        }

        if (values.Protocol is null)
        {
            return Fail(BrowserEndpointAdvertisementError.MissingBrowserProtocol, out endpoint, out error);
        }

        if (!TryParsePort(values.Port, out var port))
        {
            return Fail(BrowserEndpointAdvertisementError.InvalidBrowserPort, out endpoint, out error);
        }

        if (!TryParseProtocol(values.Protocol, out var protocolVersion, out var protocolError))
        {
            return Fail(protocolError, out endpoint, out error);
        }

        endpoint = new BrowserEndpointAdvertisement(sourceAddress, port, protocolVersion);
        error = BrowserEndpointAdvertisementError.None;
        return true;
    }

    private static bool TryAcceptAttribute(
        string? key,
        string? value,
        BrowserAttributeValues values,
        out BrowserEndpointAdvertisementError error)
    {
        if (string.IsNullOrEmpty(key))
        {
            error = BrowserEndpointAdvertisementError.MalformedTextAttribute;
            return false;
        }

        if (IsSensitiveBrowserKey(key))
        {
            error = BrowserEndpointAdvertisementError.SensitiveBrowserAttribute;
            return false;
        }

        if (string.Equals(key, BrowserPortKey, StringComparison.OrdinalIgnoreCase))
        {
            return TrySetBrowserValue(value, ref values.Port, out error);
        }

        if (string.Equals(key, BrowserProtocolKey, StringComparison.OrdinalIgnoreCase))
        {
            return TrySetBrowserValue(value, ref values.Protocol, out error);
        }

        error = BrowserEndpointAdvertisementError.None;
        return true;
    }

    private static bool TrySetBrowserValue(
        string? value,
        ref string? existingValue,
        out BrowserEndpointAdvertisementError error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = BrowserEndpointAdvertisementError.BlankBrowserAttributeValue;
            return false;
        }

        if (StrictUtf8.GetByteCount(value) > MaximumBrowserValueBytes)
        {
            error = BrowserEndpointAdvertisementError.OversizedBrowserAttributeValue;
            return false;
        }

        if (existingValue is not null)
        {
            error = string.Equals(existingValue, value, StringComparison.Ordinal)
                ? BrowserEndpointAdvertisementError.DuplicateBrowserAttribute
                : BrowserEndpointAdvertisementError.ConflictingBrowserAttribute;
            return false;
        }

        existingValue = value;
        error = BrowserEndpointAdvertisementError.None;
        return true;
    }

    private static bool TryParsePort(string value, out int port) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out port)
        && port is >= 1 and <= ushort.MaxValue;

    private static bool TryParseProtocol(
        string value,
        out int protocolVersion,
        out BrowserEndpointAdvertisementError error)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out protocolVersion)
            || protocolVersion > BrowserEndpointAdvertisement.MaximumProtocolVersion)
        {
            error = BrowserEndpointAdvertisementError.InvalidBrowserProtocol;
            return false;
        }

        if (protocolVersion < BrowserEndpointAdvertisement.MinimumSupportedProtocolVersion)
        {
            error = BrowserEndpointAdvertisementError.UnsupportedBrowserProtocol;
            return false;
        }

        error = BrowserEndpointAdvertisementError.None;
        return true;
    }

    private static bool IsBrowserKey(string key) =>
        string.Equals(key, BrowserPortKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, BrowserProtocolKey, StringComparison.OrdinalIgnoreCase);

    private static bool IsSensitiveBrowserKey(string key) => SensitiveBrowserKeys.Contains(key);

    private static bool TryValidateSelectedReceiver(
        IPAddress? selectedReceiverAddress,
        IPAddress? sourceAddress,
        out BrowserEndpointAdvertisementError error)
    {
        if (selectedReceiverAddress is null
            || !BrowserEndpointAdvertisement.IsUsableUnicastIpv4(selectedReceiverAddress))
        {
            error = BrowserEndpointAdvertisementError.InvalidSelectedReceiverAddress;
            return false;
        }

        if (sourceAddress is null || !BrowserEndpointAdvertisement.IsUsableUnicastIpv4(sourceAddress))
        {
            error = BrowserEndpointAdvertisementError.InvalidSourceAddress;
            return false;
        }

        if (!selectedReceiverAddress.Equals(sourceAddress))
        {
            error = BrowserEndpointAdvertisementError.SelectedReceiverAddressMismatch;
            return false;
        }

        error = BrowserEndpointAdvertisementError.None;
        return true;
    }

    private static bool TryGetUtf8ByteCount(string? value, out int byteCount)
    {
        byteCount = 0;
        if (value is null)
        {
            return false;
        }

        try
        {
            byteCount = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool Fail(
        BrowserEndpointAdvertisementError failure,
        out BrowserEndpointAdvertisement? endpoint,
        out BrowserEndpointAdvertisementError error)
    {
        endpoint = null;
        error = failure;
        return false;
    }

    private sealed class BrowserAttributeValues
    {
        public string? Port;

        public string? Protocol;
    }
}
