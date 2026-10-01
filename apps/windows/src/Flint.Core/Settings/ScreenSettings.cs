namespace Flint.Core.Settings;

/// <summary>The Screen sharing section of the Settings page.</summary>
/// <remarks>Each value appears on the page in the update that makes it do something.</remarks>
public sealed record ScreenSettings
{
    /// <summary>The frame rates a custom share offers.</summary>
    public static IReadOnlyList<int> FrameRates { get; } = [15, 24, 30, 60];

    /// <summary>The countdowns offered before sharing starts, in seconds; zero is none.</summary>
    public static IReadOnlyList<int> Countdowns { get; } = [0, 3, 5];

    /// <summary>The sound qualities offered, in kilobits per second.</summary>
    public static IReadOnlyList<int> SoundBitrates { get; } = [96, 128, 160, 192];

    /// <summary>The lowest custom data rate, in megabits per second.</summary>
    public const int MinimumMegabitsPerSecond = 2;

    /// <summary>The highest custom data rate, in megabits per second.</summary>
    public const int MaximumMegabitsPerSecond = 30;

    /// <summary>The longest sound delay offered, in milliseconds.</summary>
    public const int MaximumSoundDelayMilliseconds = 500;

    /// <summary>Which display a share captures.</summary>
    public ShareDisplayChoice Display { get; init; } = ShareDisplayChoice.Main;

    /// <summary>The remembered display, as Windows identifies the monitor, so a replugged one is found again.</summary>
    public string? DisplayIdentity { get; init; }

    /// <summary>Whether pressing Share asks which display to share.</summary>
    public ShareDisplayPrompt DisplayPrompt { get; init; } = ShareDisplayPrompt.UseLast;

    /// <summary>The picture a share sends.</summary>
    public PictureMode PictureMode { get; init; } = PictureMode.Balanced;

    /// <summary>The largest picture a custom share sends.</summary>
    public SizeLimit CustomSizeLimit { get; init; } = SizeLimit.P1080;

    /// <summary>The frame rate a custom share sends. One of <see cref="FrameRates"/>.</summary>
    public int CustomFramesPerSecond { get; init; } = 30;

    /// <summary>The data rate a custom share sends, in megabits per second.</summary>
    public int CustomMegabitsPerSecond { get; init; } = 12;

    /// <summary>Whether the Screen page shows the share's live counters.</summary>
    public bool ShowLiveNumbers { get; init; }

    /// <summary>The countdown before sharing starts, in seconds. One of <see cref="Countdowns"/>.</summary>
    public int CountdownSeconds { get; init; }

    /// <summary>Whether Flint minimises its window when sharing starts.</summary>
    public bool MinimiseWhenSharing { get; init; }

    /// <summary>What the TV shows while a share is paused.</summary>
    public PausedPicture PausedPicture { get; init; } = PausedPicture.LastPicture;

    /// <summary>Whether locking this PC pauses a share.</summary>
    public bool PauseWhenLocked { get; init; } = true;

    /// <summary>Whether a share paused by locking stays paused after unlocking.</summary>
    public bool StayPausedAfterUnlock { get; init; }

    /// <summary>Whether this PC's sound is shared with the picture.</summary>
    public bool ShareSound { get; init; } = true;

    /// <summary>Where shared sound plays.</summary>
    public SoundDestination SoundDestination { get; init; } = SoundDestination.TvOnly;

    /// <summary>Which sound output is shared.</summary>
    public SoundSource SoundSource { get; init; } = SoundSource.DefaultOutput;

    /// <summary>The named sound output, as Windows identifies it.</summary>
    public string? SoundDeviceIdentity { get; init; }

    /// <summary>The sound quality, in kilobits per second. One of <see cref="SoundBitrates"/>.</summary>
    public int SoundKbps { get; init; } = 128;

    /// <summary>How long sound is held back to line up with the picture, in milliseconds.</summary>
    public int SoundDelayMilliseconds { get; init; }

    /// <summary>Whether the mouse pointer is drawn into the shared picture.</summary>
    public bool ShowPointer { get; init; } = true;

    /// <summary>The same section with every value inside the range its control offers.</summary>
    public ScreenSettings Normalize() => this with
    {
        Display = SettingsRange.Defined(Display, ShareDisplayChoice.Main),
        DisplayIdentity = Clean(DisplayIdentity),
        DisplayPrompt = SettingsRange.Defined(DisplayPrompt, ShareDisplayPrompt.UseLast),
        PictureMode = SettingsRange.Defined(PictureMode, PictureMode.Balanced),
        CustomSizeLimit = SettingsRange.Defined(CustomSizeLimit, SizeLimit.P1080),
        CustomFramesPerSecond = SettingsRange.Nearest(FrameRates, CustomFramesPerSecond),
        CustomMegabitsPerSecond = Math.Clamp(CustomMegabitsPerSecond, MinimumMegabitsPerSecond, MaximumMegabitsPerSecond),
        CountdownSeconds = SettingsRange.Nearest(Countdowns, CountdownSeconds),
        PausedPicture = SettingsRange.Defined(PausedPicture, PausedPicture.LastPicture),
        SoundDestination = SettingsRange.Defined(SoundDestination, SoundDestination.TvOnly),
        SoundSource = SettingsRange.Defined(SoundSource, SoundSource.DefaultOutput),
        SoundDeviceIdentity = Clean(SoundDeviceIdentity),
        SoundKbps = SettingsRange.Nearest(SoundBitrates, SoundKbps),
        SoundDelayMilliseconds = Math.Clamp(SoundDelayMilliseconds, 0, MaximumSoundDelayMilliseconds),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
