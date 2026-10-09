using Flint.App.Services;
using Flint.Core.Settings;
using Flint.Core.Shortcuts;

namespace Flint.App.ViewModels.Settings;

/// <summary>The Shortcuts section: a row for each action, and how shortcuts work.</summary>
public sealed class ShortcutsSettingsViewModel : SettingsSectionViewModel
{
    private readonly ISettingsService settings;
    private HotKeyService? service;

    /// <summary>Creates the section over the live settings.</summary>
    public ShortcutsSettingsViewModel(ISettingsService settings)
        : base("Shortcuts")
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Rows =
        [
            new(this, ShortcutAction.StartStopSharing, "Start or stop sharing"),
            new(this, ShortcutAction.PauseResumeSharing, "Pause or resume sharing"),
            new(this, ShortcutAction.PlayPauseTv, "Play or pause on the TV"),
            new(this, ShortcutAction.NextItem, "Next in the queue"),
            new(this, ShortcutAction.PreviousItem, "Previous in the queue"),
            new(this, ShortcutAction.VolumeUp, "TV volume up"),
            new(this, ShortcutAction.VolumeDown, "TV volume down"),
            new(this, ShortcutAction.ShowWindow, "Show Flint"),
            new(this, ShortcutAction.Disconnect, "Disconnect from the TV"),
        ];
        settings.Changed += (_, change) =>
        {
            if (change.Previous.Shortcuts != change.Current.Shortcuts)
            {
                Refresh();
            }
        };
        Reset = new ConfirmableAction(
            "RESTORE DEFAULT SHORTCUTS",
            "Put every shortcut back to how Flint came?",
            "RESTORE",
            () => settings.Update(current => current with { Shortcuts = new ShortcutSettings() }));
    }

    /// <summary>Whether shortcuts work while another program is in front.</summary>
    public SettingText WorkInBackgroundText { get; } = new(
        "Shortcuts work in other programs",
        "Otherwise they work only while the Flint window is in front.");

    /// <summary>Whether the media keys control the TV.</summary>
    public SettingText MediaKeysText { get; } = new(
        "Use the keyboard's media keys for the TV",
        "Play, pause, next and previous keys control the TV instead of players on this PC.");

    /// <summary>One row for each action.</summary>
    public IReadOnlyList<ShortcutRowViewModel> Rows { get; }

    /// <summary>Puts every shortcut back.</summary>
    public ConfirmableAction Reset { get; }

    /// <inheritdoc />
    public override IReadOnlyList<SettingText> Settings =>
        [.. Rows.Select(row => row.Text), WorkInBackgroundText, MediaKeysText];

    /// <summary>Whether shortcuts work while another program is in front.</summary>
    public bool WorkInBackground
    {
        get => Current.WorkInBackground;
        set => settings.Update(current => current with { Shortcuts = current.Shortcuts with { WorkInBackground = value } });
    }

    /// <summary>Whether the media keys control the TV.</summary>
    public bool UseMediaKeys
    {
        get => Current.UseMediaKeys;
        set => settings.Update(current => current with { Shortcuts = current.Shortcuts with { UseMediaKeys = value } });
    }

    /// <summary>Whether a row is waiting for a key combination, so keys pressed now are its, not shortcuts.</summary>
    public bool IsCapturing => Rows.Any(row => row.IsCapturing);

    /// <summary>The shortcuts in force.</summary>
    internal ShortcutSettings Current => settings.Current.Shortcuts;

    /// <summary>Gives the section the service that claims shortcuts from Windows.</summary>
    internal void UseService(HotKeyService hotKeys)
    {
        if (service is not null)
        {
            service.ProblemsChanged -= OnProblemsChanged;
        }

        service = hotKeys ?? throw new ArgumentNullException(nameof(hotKeys));
        service.ProblemsChanged += OnProblemsChanged;
        Refresh();
    }

    /// <summary>Why <paramref name="action"/>'s shortcut is not working, or null.</summary>
    internal string? ProblemFor(ShortcutAction action) => service?.Problems.GetValueOrDefault(action);

    /// <summary>Starts capturing a new shortcut for one row, and stops any other.</summary>
    internal void BeginCapture(ShortcutRowViewModel capturing)
    {
        foreach (var row in Rows)
        {
            if (row != capturing)
            {
                row.CancelCapture();
            }
        }
    }

    /// <summary>The row whose action already has <paramref name="gesture"/>, other than <paramref name="except"/>.</summary>
    internal ShortcutRowViewModel? OwnerOf(HotKeyGesture gesture, ShortcutAction except) =>
        Rows.FirstOrDefault(row => row.Action != except
            && HotKeyGesture.TryParse(HotKeyService.TextFor(Current, row.Action), out var held, out _)
            && held == gesture);

    /// <summary>Gives an action a shortcut: through Windows when there is a service, or straight into the settings.</summary>
    /// <returns>Why it could not, or null.</returns>
    internal string? Assign(ShortcutAction action, HotKeyGesture gesture)
    {
        if (service is not null)
        {
            return service.TryAssign(action, gesture);
        }

        Set(action, gesture.ToString());
        return null;
    }

    /// <summary>Sets an action's shortcut text, or clears it with null.</summary>
    internal void Set(ShortcutAction action, string? text) =>
        settings.Update(current => current with { Shortcuts = HotKeyService.With(current.Shortcuts, action, text) });

    private void OnProblemsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(string.Empty);
        foreach (var row in Rows)
        {
            row.Refresh();
        }
    }
}
