namespace Flint.Core.Settings;

/// <summary>
/// Everything a person can change about how Flint behaves, as one immutable value.
/// </summary>
/// <remarks>
/// <para>
/// Grouped by the Settings page section each value appears in, so a section can be reset on its own
/// and a damaged section in the saved file costs only that section.
/// </para>
/// <para>
/// Every value that reaches the rest of the app has been through <see cref="Normalize"/>. A file
/// edited by hand, written by a newer Flint, or half-written by a crash can therefore never put the
/// app into a state its own controls could not have produced.
/// </para>
/// <para>
/// Sections for features that have not shipped are kept here already, so a person's choices
/// survive the update that introduces them. Their controls appear on the Settings page only once
/// the feature they govern works.
/// </para>
/// </remarks>
public sealed record AppSettings
{
    /// <summary>The shape of the saved file this build writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Every setting as Flint ships.</summary>
    public static AppSettings Default { get; } = new();

    /// <summary>The shape of the file these settings were read from.</summary>
    /// <remarks>
    /// A newer number than <see cref="CurrentSchemaVersion"/> is read rather than refused: the
    /// fields this build knows are taken and the rest ignored, which is how a downgrade keeps a
    /// person's settings.
    /// </remarks>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The General section.</summary>
    public GeneralSettings General { get; init; } = new();

    /// <summary>The Screen sharing section.</summary>
    public ScreenSettings Screen { get; init; } = new();

    /// <summary>The Media section.</summary>
    public MediaSettings Media { get; init; } = new();

    /// <summary>The Shortcuts section.</summary>
    public ShortcutSettings Shortcuts { get; init; } = new();

    /// <summary>The tray icon's behaviour.</summary>
    public TraySettings Tray { get; init; } = new();

    /// <summary>The same settings with every value inside the range its control offers.</summary>
    public AppSettings Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        General = (General ?? new GeneralSettings()).Normalize(),
        Screen = (Screen ?? new ScreenSettings()).Normalize(),
        Media = (Media ?? new MediaSettings()).Normalize(),
        Shortcuts = (Shortcuts ?? new ShortcutSettings()).Normalize(),
        Tray = Tray ?? new TraySettings(),
    };
}

/// <summary>The General section of the Settings page.</summary>
public sealed record GeneralSettings
{
    /// <summary>The interface sizes offered, as percentages of Flint's own size.</summary>
    public static IReadOnlyList<int> InterfaceScales { get; } = [90, 100, 115, 130];

    /// <summary>The shortest time Flint keeps trying to reach a TV that went away.</summary>
    public const int MinimumReconnectSeconds = 30;

    /// <summary>The longest time Flint keeps trying to reach a TV that went away.</summary>
    public const int MaximumReconnectSeconds = 600;

    /// <summary>
    /// Whether Flint asks before replacing what the TV shows with something else.
    /// </summary>
    /// <remarks>
    /// Off means a request to share the screen, play a file or open the browser simply takes the TV.
    /// Nothing that happens on its own ever takes the TV, whichever way this is set.
    /// </remarks>
    public bool AskBeforeSwitching { get; init; } = true;

    /// <summary>Whether this PC is kept from sleeping while something from it is on the TV.</summary>
    public bool KeepAwake { get; init; } = true;

    /// <summary>How large Flint draws itself, as a percentage. One of <see cref="InterfaceScales"/>.</summary>
    public int InterfaceScalePercent { get; init; } = 100;

    /// <summary>Whether Flint reconnects to the last TV when it starts.</summary>
    public bool ReconnectOnStart { get; init; } = true;

    /// <summary>Whether Flint keeps trying when the connection to the TV drops.</summary>
    public bool ReconnectAfterDrop { get; init; } = true;

    /// <summary>How long Flint keeps trying after a drop, in seconds.</summary>
    public int ReconnectSeconds { get; init; } = 120;

    /// <summary>What Flint does once a dropped connection comes back.</summary>
    public ReconnectOutcome AfterReconnect { get; init; } = ReconnectOutcome.Ask;

    /// <summary>Whether Flint opens its TV app when the TV needs a new pairing code.</summary>
    public bool OpenReceiverForNewCode { get; init; } = true;

    /// <summary>Whether Flint starts when the person signs in to Windows.</summary>
    public bool StartWithWindows { get; init; }

    /// <summary>Whether Flint starts in the tray without opening its window.</summary>
    public bool StartInTray { get; init; }

    /// <summary>What closing the window does.</summary>
    public CloseWindowOutcome CloseWindow { get; init; } = CloseWindowOutcome.Ask;

    /// <summary>Whether quitting asks first while something from this PC is on the TV.</summary>
    public bool ConfirmQuitWhileActive { get; init; } = true;

    /// <summary>The same section with every value inside the range its control offers.</summary>
    public GeneralSettings Normalize() => this with
    {
        InterfaceScalePercent = SettingsRange.Nearest(InterfaceScales, InterfaceScalePercent),
        ReconnectSeconds = Math.Clamp(ReconnectSeconds, MinimumReconnectSeconds, MaximumReconnectSeconds),
        AfterReconnect = SettingsRange.Defined(AfterReconnect, ReconnectOutcome.Ask),
        CloseWindow = SettingsRange.Defined(CloseWindow, CloseWindowOutcome.Ask),
    };
}
