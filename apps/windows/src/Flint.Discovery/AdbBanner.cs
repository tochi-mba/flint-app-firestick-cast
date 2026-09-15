namespace Flint.Discovery;

/// <summary>
/// The properties a device reports in its connect banner.
/// </summary>
/// <remarks>
/// A banner looks like <c>device::ro.product.name=foo;ro.product.model=bar;...</c>. Fields are
/// optional and vendors vary, so every property here is nullable and nothing is inferred from
/// anything else.
/// </remarks>
/// <param name="Raw">The banner exactly as received, kept for Diagnostics.</param>
/// <param name="Properties">Parsed key-value pairs.</param>
public sealed record AdbBanner(string Raw, IReadOnlyDictionary<string, string> Properties)
{
    /// <summary>The device model, when reported.</summary>
    public string? Model => Lookup("ro.product.model");

    /// <summary>The device name, when reported.</summary>
    public string? Name => Lookup("ro.product.name");

    /// <summary>The device identifier, when reported.</summary>
    public string? Device => Lookup("ro.product.device");

    private string? Lookup(string key) =>
        Properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>
    /// Parses a connect banner.
    /// </summary>
    /// <remarks>
    /// Tolerant by design. A malformed or truncated banner yields whatever could be read rather
    /// than throwing, because a device that half-answered is still evidence that it is Android.
    /// </remarks>
    public static AdbBanner Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var trimmed = raw.TrimEnd('\0');
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        var separator = trimmed.IndexOf("::", StringComparison.Ordinal);
        var body = separator >= 0 ? trimmed[(separator + 2)..] : trimmed;

        foreach (var entry in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                continue;
            }

            var key = entry[..equals].Trim();
            var value = entry[(equals + 1)..].Trim();
            if (key.Length > 0)
            {
                properties[key] = value;
            }
        }

        return new AdbBanner(trimmed, properties);
    }
}
