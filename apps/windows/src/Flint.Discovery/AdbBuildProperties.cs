namespace Flint.Discovery;

/// <summary>The small read-only subset Flint uses from <c>adb shell getprop</c>.</summary>
internal sealed record AdbBuildProperties(
    int? AndroidApiLevel,
    string? AndroidRelease,
    string? Model)
{
    internal static AdbBuildProperties Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            const string separator = "]: [";
            if (!line.StartsWith('[') || !line.EndsWith(']'))
            {
                continue;
            }

            var split = line.IndexOf(separator, StringComparison.Ordinal);
            if (split <= 1)
            {
                continue;
            }

            var key = line[1..split];
            var valueStart = split + separator.Length;
            properties[key] = line[valueStart..^1];
        }

        var apiLevel = properties.TryGetValue("ro.build.version.sdk", out var sdk)
            && int.TryParse(
                sdk,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                ? (int?)parsed
                : null;
        properties.TryGetValue("ro.build.version.release", out var androidRelease);
        properties.TryGetValue("ro.product.model", out var model);

        return new AdbBuildProperties(
            apiLevel,
            NullIfBlank(androidRelease),
            NullIfBlank(model));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
