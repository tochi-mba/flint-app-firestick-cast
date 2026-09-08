using System.Reflection;

namespace Flint.Cli;

/// <summary>
/// What build this is.
/// </summary>
/// <remarks>
/// Read from the assembly rather than a hand-kept literal, so it cannot drift from what was
/// actually shipped. Bug reports quote it, and a version string that lies about the build is worse
/// than none at all.
/// </remarks>
internal static class FlintVersion
{
    /// <summary>One line: name, version, and the runtime it is running on.</summary>
    internal static string Describe()
    {
        var assembly = typeof(FlintVersion).Assembly;

        // InformationalVersion carries the source revision when the build stamps one; the plain
        // version does not. Prefer it, and fall back rather than printing nothing.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        return $"flint {version} ({Environment.OSVersion.VersionString}, .NET {Environment.Version})";
    }
}
