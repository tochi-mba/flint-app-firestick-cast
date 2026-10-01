namespace Flint.Core.Settings;

/// <summary>The Shortcuts section of the Settings page.</summary>
/// <remarks>
/// Each shortcut is kept as the text it is shown as, such as <c>Ctrl+Alt+Shift+S</c>; null means
/// the action has no shortcut. Whether a combination is usable is decided when it is registered,
/// because only Windows knows whether another program already owns it.
/// </remarks>
public sealed record ShortcutSettings
{
    /// <summary>Starts or stops sharing the screen.</summary>
    public string? StartStopSharing { get; init; } = "Ctrl+Alt+Shift+S";

    /// <summary>Pauses or resumes sharing the screen.</summary>
    public string? PauseResumeSharing { get; init; } = "Ctrl+Alt+Shift+P";

    /// <summary>Plays or pauses what is on the TV.</summary>
    public string? PlayPauseTv { get; init; } = "Ctrl+Alt+Shift+Space";

    /// <summary>Plays the next item in the queue.</summary>
    public string? NextItem { get; init; } = "Ctrl+Alt+Shift+Right";

    /// <summary>Plays the previous item in the queue.</summary>
    public string? PreviousItem { get; init; } = "Ctrl+Alt+Shift+Left";

    /// <summary>Turns the TV's volume up.</summary>
    public string? VolumeUp { get; init; } = "Ctrl+Alt+Shift+Up";

    /// <summary>Turns the TV's volume down.</summary>
    public string? VolumeDown { get; init; } = "Ctrl+Alt+Shift+Down";

    /// <summary>Brings the Flint window forward.</summary>
    public string? ShowWindow { get; init; } = "Ctrl+Alt+Shift+F";

    /// <summary>Disconnects from the TV. Unassigned until the person chooses one.</summary>
    public string? Disconnect { get; init; }

    /// <summary>Whether the shortcuts work while another program is in front.</summary>
    public bool WorkInBackground { get; init; } = true;

    /// <summary>Whether the keyboard's media keys control the TV.</summary>
    /// <remarks>Off by default: another player on this PC may already own them.</remarks>
    public bool UseMediaKeys { get; init; }

    /// <summary>The same section with blank shortcuts unassigned and the rest trimmed.</summary>
    public ShortcutSettings Normalize() => this with
    {
        StartStopSharing = Clean(StartStopSharing),
        PauseResumeSharing = Clean(PauseResumeSharing),
        PlayPauseTv = Clean(PlayPauseTv),
        NextItem = Clean(NextItem),
        PreviousItem = Clean(PreviousItem),
        VolumeUp = Clean(VolumeUp),
        VolumeDown = Clean(VolumeDown),
        ShowWindow = Clean(ShowWindow),
        Disconnect = Clean(Disconnect),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>The tray icon's behaviour.</summary>
public sealed record TraySettings
{
    /// <summary>Whether Flint shows an icon in the notification area.</summary>
    public bool ShowIcon { get; init; } = true;

    /// <summary>Whether one click on the icon opens Flint, rather than a double click.</summary>
    public bool SingleClickOpens { get; init; } = true;
}
