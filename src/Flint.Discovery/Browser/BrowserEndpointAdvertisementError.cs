namespace Flint.Discovery.Browser;

/// <summary>
/// Why untrusted TXT metadata could not be used as a browser endpoint route.
/// </summary>
/// <remarks>
/// These values describe routing validation only. None of them establish receiver identity,
/// certificate trust, or browser-session authorization.
/// </remarks>
public enum BrowserEndpointAdvertisementError
{
    /// <summary>The advertisement is complete and usable as untrusted routing metadata.</summary>
    None = 0,

    /// <summary>The advertised endpoint source is not a usable IPv4 unicast address.</summary>
    InvalidSourceAddress = 1,

    /// <summary>The receiver selected by the user is not a usable IPv4 unicast address.</summary>
    InvalidSelectedReceiverAddress = 2,

    /// <summary>The advertisement belongs to a different receiver than the selected one.</summary>
    SelectedReceiverAddressMismatch = 3,

    /// <summary>The source supplied more TXT attributes than this bounded parser accepts.</summary>
    TooManyTextAttributes = 4,

    /// <summary>A TXT attribute is malformed or cannot be represented as strict UTF-8.</summary>
    MalformedTextAttribute = 5,

    /// <summary>A TXT attribute exceeds the DNS-SD string bound.</summary>
    OversizedTextAttribute = 6,

    /// <summary>A browser-specific key was supplied without a valid key-value form.</summary>
    MalformedBrowserAttribute = 7,

    /// <summary>A required browser-specific value is blank or whitespace only.</summary>
    BlankBrowserAttributeValue = 8,

    /// <summary>A required browser-specific value exceeds its fixed small limit.</summary>
    OversizedBrowserAttributeValue = 9,

    /// <summary>The browser port attribute is absent.</summary>
    MissingBrowserPort = 10,

    /// <summary>The browser port is not a decimal TCP port in the valid range.</summary>
    InvalidBrowserPort = 11,

    /// <summary>The browser protocol attribute is absent.</summary>
    MissingBrowserProtocol = 12,

    /// <summary>The browser protocol version is not a bounded decimal wire version.</summary>
    InvalidBrowserProtocol = 13,

    /// <summary>The browser protocol version predates the TLS-only browser protocol.</summary>
    UnsupportedBrowserProtocol = 14,

    /// <summary>A browser port or protocol key was repeated with the same value.</summary>
    DuplicateBrowserAttribute = 15,

    /// <summary>A browser port or protocol key was repeated with a different value.</summary>
    ConflictingBrowserAttribute = 16,

    /// <summary>The record attempted to carry browser identity or authorization material.</summary>
    SensitiveBrowserAttribute = 17,
}
