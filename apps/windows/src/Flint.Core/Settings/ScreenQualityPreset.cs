namespace Flint.Core.Settings;

/// <summary>What a picture mode sends.</summary>
/// <param name="SizeLimit">The largest picture.</param>
/// <param name="FramesPerSecond">The most frames a second.</param>
/// <param name="MegabitsPerSecond">The data rate.</param>
public sealed record ScreenQuality(SizeLimit SizeLimit, int FramesPerSecond, int MegabitsPerSecond);

/// <summary>
/// The picture modes, in one table: what each sends, and the sentence that says so.
/// </summary>
/// <remarks>
/// The sentence under each mode is built from the same numbers a share is started with, so what
/// the Screen page says and what the TV gets cannot drift apart. No sentence mentions delay,
/// because none of these has been measured for it.
/// </remarks>
public static class ScreenQualityPreset
{
    /// <summary>The widest picture when the TV's own size is not known.</summary>
    public const uint FallbackWidth = 1920;

    private static readonly Dictionary<PictureMode, (ScreenQuality Quality, string Purpose)> Table = new()
    {
        [PictureMode.Balanced] = (new(SizeLimit.P1080, 30, 12), "For everyday use"),
        [PictureMode.Movie] = (new(SizeLimit.P1080, 30, 20), "A cleaner picture for video"),
        [PictureMode.Game] = (new(SizeLimit.P1080, 60, 16), "Smoother motion"),
        [PictureMode.TextAndSlides] = (new(SizeLimit.P1080, 15, 12), "Sharp text with little motion"),
        [PictureMode.DataSaver] = (new(SizeLimit.P720, 30, 4), "For weak Wi-Fi"),
    };

    /// <summary>The modes with fixed values, in the order the Screen page offers them.</summary>
    public static IReadOnlyList<PictureMode> FixedModes { get; } =
        [PictureMode.Balanced, PictureMode.Movie, PictureMode.Game, PictureMode.TextAndSlides, PictureMode.DataSaver];

    /// <summary>What <paramref name="settings"/> asks a share to send.</summary>
    public static ScreenQuality Resolve(ScreenSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Table.TryGetValue(settings.PictureMode, out var row)
            ? row.Quality
            : new ScreenQuality(settings.CustomSizeLimit, settings.CustomFramesPerSecond, settings.CustomMegabitsPerSecond);
    }

    /// <summary>The widest picture a size limit allows, never wider than the TV.</summary>
    /// <param name="limit">The size limit.</param>
    /// <param name="tvWidth">The TV's own width, when it reported one.</param>
    /// <param name="displayWidth">The shared display's own width, when known.</param>
    /// <returns>A width in pixels; zero means the display's own size with no limit.</returns>
    public static uint MaxWidth(SizeLimit limit, int? tvWidth, int? displayWidth)
    {
        var tv = tvWidth is > 1 ? (uint)tvWidth.Value : 0;
        uint wanted = limit switch
        {
            SizeLimit.P720 => 1280,
            SizeLimit.MatchTv => tv is 0 ? FallbackWidth : tv,
            SizeLimit.Native => displayWidth is > 1 ? (uint)displayWidth.Value : 0,
            _ => 1920,
        };

        // A picture larger than the TV's screen spends data and decoding on pixels it throws away.
        return tv is 0 ? wanted : wanted is 0 ? tv : Math.Min(wanted, tv);
    }

    /// <summary>The options a share starts with.</summary>
    /// <param name="settings">The Screen sharing settings.</param>
    /// <param name="outputIndex">The display to capture.</param>
    /// <param name="tvWidth">The TV's own width, when it reported one.</param>
    /// <param name="displayWidth">The shared display's own width, when known.</param>
    public static MirrorSessionOptions ToOptions(ScreenSettings settings, uint outputIndex, int? tvWidth, int? displayWidth)
    {
        var quality = Resolve(settings);
        return new MirrorSessionOptions(
            outputIndex,
            (uint)quality.FramesPerSecond,
            (uint)quality.MegabitsPerSecond * 1_000_000,
            MaxWidth(quality.SizeLimit, tvWidth, displayWidth));
    }

    /// <summary>
    /// One sentence saying what a mode is for and exactly what it sends, such as
    /// "For everyday use. Up to 1080p, 30 frames a second, 12 Mbps."
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <param name="options">The options that mode starts a share with.</param>
    public static string Describe(PictureMode mode, MirrorSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var purpose = Table.TryGetValue(mode, out var row) ? row.Purpose : "Your own size, frame rate and data rate";
        return $"{purpose}. Up to {DescribeWidth(options.MaxWidth)}, {options.FrameRate} frames a second, "
            + $"{options.BitrateBitsPerSecond / 1_000_000} Mbps.";
    }

    /// <summary>A width as people name picture sizes.</summary>
    public static string DescribeWidth(uint width) => width switch
    {
        0 => "the display's own size",
        1280 => "720p",
        1920 => "1080p",
        2560 => "1440p",
        3840 => "4K",
        _ => $"{width} pixels wide",
    };
}
