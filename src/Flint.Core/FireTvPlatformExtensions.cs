namespace Flint.Core;

/// <summary>
/// Platform predicates used by <see cref="CapabilityAssessor"/> and the UI.
/// </summary>
public static class FireTvPlatformExtensions
{
    /// <summary>
    /// Whether a custom receiver can be installed on this platform.
    /// </summary>
    /// <remarks>
    /// <see cref="FireTvPlatform.Unknown"/> returns <see langword="false"/> deliberately. An
    /// undetermined platform is not a capability, and reporting one would be a guess.
    /// </remarks>
    public static bool CanInstallReceiver(this FireTvPlatform platform) =>
        platform.IsAndroidBased();

    /// <summary>Whether the platform is Android-based and therefore exposes ADB.</summary>
    public static bool IsAndroidBased(this FireTvPlatform platform) =>
        platform is FireTvPlatform.FireOs5
            or FireTvPlatform.FireOs6
            or FireTvPlatform.FireOs7
            or FireTvPlatform.FireOs8
            or FireTvPlatform.FireOs14
            or FireTvPlatform.FireOs16;

    /// <summary>A short label for display. Upper-cased at the view layer, not here.</summary>
    public static string ToDisplayLabel(this FireTvPlatform platform) => platform switch
    {
        FireTvPlatform.FireOs5 => "Fire OS 5",
        FireTvPlatform.FireOs6 => "Fire OS 6",
        FireTvPlatform.FireOs7 => "Fire OS 7",
        FireTvPlatform.FireOs8 => "Fire OS 8",
        FireTvPlatform.FireOs14 => "Fire OS 14",
        FireTvPlatform.FireOs16 => "Fire OS 16",
        FireTvPlatform.Vega => "Vega OS",
        _ => "Unknown",
    };
}
