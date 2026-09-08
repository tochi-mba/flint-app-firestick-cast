using Flint.Core;

namespace Flint.Discovery;

/// <summary>
/// Turns probe evidence into a <see cref="FireTvPlatform"/>.
/// </summary>
/// <remarks>
/// Pure, and deliberately conservative. Every path that lacks proof returns
/// <see cref="FireTvPlatform.Unknown"/> rather than the likelier answer, because the whole value of
/// the prober is that it does not guess.
/// </remarks>
public static class FireTvPlatformResolver
{
    /// <summary>Build models Amazon documents as shipping with Vega OS rather than Android.</summary>
    /// <remarks>
    /// Exact identifiers only. A product nickname containing words such as "Select" is not strong
    /// enough evidence to make an irreversible platform verdict.
    /// </remarks>
    public static IReadOnlySet<string> KnownVegaBuildModels { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AFTCA002", // Fire TV Stick 4K Select (2025)
            "AFTCL001", // Fire TV Stick HD, second generation (2026)
        };

    /// <summary>
    /// Resolves the platform from a probe result.
    /// </summary>
    /// <param name="result">What the ADB probe proved.</param>
    /// <param name="apiLevel">
    /// The device's Android API level when it could be read. This is matched only to API ranges
    /// Amazon documents for a Fire OS generation; an undocumented level remains unknown.
    /// </param>
    public static FireTvPlatform Resolve(AdbProbeResult result, int? apiLevel, string? buildModel = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (buildModel is not null && KnownVegaBuildModels.Contains(buildModel))
        {
            return FireTvPlatform.Vega;
        }

        if (!result.ProvesAndroid)
        {
            // Silence is ambiguous: it could be Vega, or Fire OS with debugging off. Say so.
            return FireTvPlatform.Unknown;
        }

        // Amazon publishes these API-level ranges for Fire OS. Values outside the documented set
        // stay unknown rather than being rounded into the nearest generation.
        return apiLevel switch
        {
            22 => FireTvPlatform.FireOs5,
            25 => FireTvPlatform.FireOs6,
            28 => FireTvPlatform.FireOs7,
            29 or 30 => FireTvPlatform.FireOs8,
            >= 31 and <= 34 => FireTvPlatform.FireOs14,
            35 or 36 => FireTvPlatform.FireOs16,
            _ => FireTvPlatform.Unknown,
        };
    }
}
