namespace Flint.Core.Settings;

/// <summary>The Media section of the Settings page: playback, the queue and resuming.</summary>
/// <remarks>Each value appears on the page in the update that makes it do something.</remarks>
public sealed record MediaSettings
{
    /// <summary>The skip-back distances offered, in seconds.</summary>
    public static IReadOnlyList<int> SkipBackChoices { get; } = [5, 10, 15, 30];

    /// <summary>The skip-forward distances offered, in seconds.</summary>
    public static IReadOnlyList<int> SkipForwardChoices { get; } = [10, 15, 30, 60];

    /// <summary>The shortest time a picture can be shown for, in seconds.</summary>
    /// <remarks>The TV ends a picture after six seconds on its own, so nothing shorter can be honoured.</remarks>
    public const int MinimumPictureSeconds = 6;

    /// <summary>The longest time a picture can be shown for, in seconds.</summary>
    public const int MaximumPictureSeconds = 60;

    /// <summary>How far back the skip button goes, in seconds. One of <see cref="SkipBackChoices"/>.</summary>
    public int SkipBackSeconds { get; init; } = 10;

    /// <summary>How far forward the skip button goes, in seconds. One of <see cref="SkipForwardChoices"/>.</summary>
    public int SkipForwardSeconds { get; init; } = 30;

    /// <summary>How much one volume press changes the TV's volume, in percent.</summary>
    public int VolumeStepPercent { get; init; } = 5;

    /// <summary>
    /// Whether the time on the right of the seek bar is the file's whole length rather than the
    /// time left. Changed by clicking that time, not on the Settings page.
    /// </summary>
    public bool ShowTotalTime { get; init; }

    /// <summary>Whether the next item in the queue plays by itself.</summary>
    public bool AutoPlayNext { get; init; } = true;

    /// <summary>How the queue repeats.</summary>
    public RepeatMode Repeat { get; init; } = RepeatMode.Off;

    /// <summary>Whether the queue plays in a shuffled order.</summary>
    public bool Shuffle { get; init; }

    /// <summary>What happens when a file played before is played again.</summary>
    public ResumeMode PlayedBefore { get; init; } = ResumeMode.Ask;

    /// <summary>Whether Flint remembers what was played and where it stopped.</summary>
    public bool RememberPlayback { get; init; } = true;

    /// <summary>Whether a dropped folder's subfolders are included.</summary>
    public bool IncludeSubfolders { get; init; }

    /// <summary>The order dropped files are queued in.</summary>
    public DropOrder DropOrder { get; init; } = DropOrder.ByName;

    /// <summary>How long a picture in the queue is shown, in seconds; zero waits for the person.</summary>
    public int PictureSeconds { get; init; }

    /// <summary>What the TV shows once the queue has finished.</summary>
    public QueueEnd QueueEnd { get; init; } = QueueEnd.LeaveLastItem;

    /// <summary>The same section with every value inside the range its control offers.</summary>
    public MediaSettings Normalize() => this with
    {
        SkipBackSeconds = SettingsRange.Nearest(SkipBackChoices, SkipBackSeconds),
        SkipForwardSeconds = SettingsRange.Nearest(SkipForwardChoices, SkipForwardSeconds),
        VolumeStepPercent = Math.Clamp(VolumeStepPercent, 1, 10),
        Repeat = SettingsRange.Defined(Repeat, RepeatMode.Off),
        PlayedBefore = SettingsRange.Defined(PlayedBefore, ResumeMode.Ask),
        DropOrder = SettingsRange.Defined(DropOrder, DropOrder.ByName),
        PictureSeconds = PictureSeconds <= 0 ? 0 : Math.Clamp(PictureSeconds, MinimumPictureSeconds, MaximumPictureSeconds),
        QueueEnd = SettingsRange.Defined(QueueEnd, QueueEnd.LeaveLastItem),
    };
}
