using Flint.Core;

namespace Flint.Discovery;

/// <summary>Reads what the television's package manager prints, and nothing it did not print.</summary>
public static class AdbPackageInventory
{
    /// <summary>The prefix every Flint package shares, so one listing covers every name a receiver has shipped under.</summary>
    public const string PackagePrefix = "com.rextechnologies.flint";

    private const string PackageLine = "package:";

    /// <summary>The package names in a <c>pm list packages</c> listing.</summary>
    public static IReadOnlySet<string> ParseListing(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var packages = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith(PackageLine, StringComparison.Ordinal))
            {
                continue;
            }

            var name = line[PackageLine.Length..].Trim();
            if (name.Length > 0)
            {
                packages.Add(name);
            }
        }

        return packages;
    }

    /// <summary>
    /// The installed version of one package, from <c>dumpsys package</c>.
    /// </summary>
    /// <remarks>
    /// A version the dump does not carry stays unknown. Inventing one would let a later comparison
    /// "update" a television whose build nobody actually read.
    /// </remarks>
    public static InstalledReceiver ParseDump(string packageName, string dump)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(dump);

        string? versionName = null;
        long? versionCode = null;
        foreach (var rawLine in dump.Split('\n'))
        {
            var line = rawLine.Trim();
            if (versionName is null && line.StartsWith("versionName=", StringComparison.Ordinal))
            {
                var value = line["versionName=".Length..].Trim();
                versionName = value.Length == 0 || value == "null" ? null : value;
            }
            else if (versionCode is null && line.StartsWith("versionCode=", StringComparison.Ordinal))
            {
                var value = line["versionCode=".Length..];
                var end = value.IndexOf(' ', StringComparison.Ordinal);
                var number = end < 0 ? value : value[..end];
                if (long.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    versionCode = parsed;
                }
            }
        }

        return new InstalledReceiver(packageName, versionName, versionCode);
    }
}
