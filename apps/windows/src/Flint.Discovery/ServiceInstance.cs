using System.Net;
using Flint.Discovery.Browser;

namespace Flint.Discovery;

/// <summary>
/// A service instance assembled from the records in one or more mDNS responses.
/// </summary>
/// <param name="InstanceName">The instance name, without the service-type suffix.</param>
/// <param name="Address">The resolved IPv4 address.</param>
/// <param name="Port">The advertised port.</param>
/// <param name="Attributes">Parsed TXT attributes (last value wins for duplicate keys).</param>
/// <param name="RawTextAttributes">Ordered raw TXT strings, including bare flags.</param>
/// <param name="HasMalformedTextAttributes">True when any TXT entry failed UTF-8 / length checks.</param>
public sealed record ServiceInstance(
    string InstanceName,
    IPAddress Address,
    int Port,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<string> RawTextAttributes,
    bool HasMalformedTextAttributes = false)
{
    /// <summary>Compatibility constructor for callers that only need the dictionary view.</summary>
    public ServiceInstance(
        string instanceName,
        IPAddress address,
        int port,
        IReadOnlyDictionary<string, string> attributes)
        : this(instanceName, address, port, attributes, Array.Empty<string>(), false)
    {
    }

    /// <summary>An attribute value, or <see langword="null"/> when absent or empty.</summary>
    public string? Attribute(string key) =>
        Attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Attempts to parse the untrusted browser TLS route from this instance's TXT evidence.
    /// </summary>
    public bool TryResolveBrowserEndpoint(
        IPAddress? sourceAddress,
        out BrowserEndpointAdvertisement? endpoint,
        out BrowserEndpointAdvertisementError error)
    {
        if (HasMalformedTextAttributes)
        {
            endpoint = null;
            error = BrowserEndpointAdvertisementError.MalformedTextAttribute;
            return false;
        }

        return BrowserEndpointAdvertisementParser.TryParse(
            sourceAddress ?? Address,
            RawTextAttributes.Count > 0
                ? RawTextAttributes
                : [.. Attributes.Select(pair => $"{pair.Key}={pair.Value}")],
            out endpoint,
            out error);
    }
}
