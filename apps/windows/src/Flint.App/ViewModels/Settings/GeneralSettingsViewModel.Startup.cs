using CommunityToolkit.Mvvm.Input;
using Flint.Core.Settings;
using Flint.Platform.Windows;

namespace Flint.App.ViewModels.Settings;

/// <summary>One answer to "when I close the window".</summary>
/// <param name="Value">The answer.</param>
/// <param name="Label">Its name in the list.</param>
public sealed record CloseWindowChoice(CloseWindowOutcome Value, string Label)
{
    /// <inheritdoc />
    public override string ToString() => Label;
}

/// <summary>Starting with Windows, the tray icon, and what closing the window does.</summary>
public sealed partial class GeneralSettingsViewModel
{
    private RunAtSignIn? signIn;

    /// <summary>The tray icon.</summary>
    public SettingText ShowTrayIconText { get; } = new(
        "Show Flint in the notification area",
        "An icon by the clock that says what Flint is doing, with a menu to share, pause and quit.");

    /// <summary>How the icon opens Flint.</summary>
    public SettingText SingleClickText { get; } = new(
        "Open Flint with one click on its icon",
        "Otherwise it takes a double click, and one click does nothing.");

    /// <summary>Starting at sign-in.</summary>
    public SettingText StartWithWindowsText { get; } = new(
        "Start Flint when I sign in to Windows",
        "So Flint is ready, and reconnects to your TV, before you go looking for it.");

    /// <summary>Starting hidden.</summary>
    public SettingText StartInTrayText { get; } = new(
        "Start in the tray",
        "When Flint starts at sign-in, it waits in the notification area rather than opening its window.");

    /// <summary>What closing the window does.</summary>
    public SettingText CloseWindowText { get; } = new(
        "When I close the window",
        "Keep Flint running in the tray, so sharing and playback carry on, or quit it.");

    /// <summary>Asking before quitting.</summary>
    public SettingText ConfirmQuitText { get; } = new(
        "Ask before quitting while something is on the TV",
        "Quitting stops a share or a file that is playing; Flint checks first.");

    /// <summary>The answers to "when I close the window".</summary>
    public IReadOnlyList<CloseWindowChoice> CloseWindowChoices { get; } =
    [
        new(CloseWindowOutcome.Ask, "Ask me the first time"),
        new(CloseWindowOutcome.KeepRunning, "Keep running in the tray"),
        new(CloseWindowOutcome.Quit, "Quit Flint"),
    ];

    /// <summary>Whether the tray icon shows.</summary>
    public bool ShowTrayIcon
    {
        get => settings.Current.Tray.ShowIcon;
        set => settings.Update(current => current with { Tray = current.Tray with { ShowIcon = value } });
    }

    /// <summary>Whether one click on the icon opens Flint.</summary>
    public bool SingleClickOpens
    {
        get => settings.Current.Tray.SingleClickOpens;
        set => settings.Update(current => current with { Tray = current.Tray with { SingleClickOpens = value } });
    }

    /// <summary>What closing the window does, in use.</summary>
    /// <remarks>Without the icon, closing always quits, so the row shows that and cannot be changed.</remarks>
    public CloseWindowChoice CloseWindow
    {
        get => CloseWindowChoices.First(choice => choice.Value == (ShowTrayIcon ? settings.Current.General.CloseWindow : CloseWindowOutcome.Quit));
        set
        {
            if (value is not null)
            {
                settings.Update(current => current with { General = current.General with { CloseWindow = value.Value } });
            }
        }
    }

    /// <summary>Why the closing row cannot be changed, or null when it can.</summary>
    public string? CloseWindowLocked => ShowTrayIcon ? null : "Without the tray icon, closing the window quits Flint.";

    /// <summary>Whether quitting asks first while something is on the TV.</summary>
    public bool ConfirmQuit
    {
        get => settings.Current.General.ConfirmQuitWhileActive;
        set => settings.Update(current => current with { General = current.General with { ConfirmQuitWhileActive = value } });
    }

    /// <summary>Whether starting at sign-in can be offered: only where Flint can write its entry.</summary>
    public bool CanStartWithWindows => signIn is not null;

    /// <summary>Whether Flint starts at sign-in, as Windows has it now.</summary>
    public bool StartWithWindows
    {
        get => signIn?.Read() is SignInStart.On or SignInStart.InTray;
        set => SetStartWithWindows(value);
    }

    /// <summary>Whether Flint starts in the tray at sign-in.</summary>
    public bool StartInTray
    {
        get => settings.Current.General.StartInTray;
        set
        {
            settings.Update(current => current with { General = current.General with { StartInTray = value } });
            if (StartWithWindows)
            {
                signIn!.Enable(value);
            }
        }
    }

    /// <summary>What Windows starts at sign-in, shown so the person can see exactly what runs.</summary>
    public string? StartsFile => signIn is null ? null : $"Starts {signIn.Executable}";

    /// <summary>Said when the entry names a copy of Flint that is no longer there.</summary>
    public string? StaleStart => signIn?.Read() is SignInStart.Stale
        ? "Flint was set to start from a copy that is no longer there."
        : null;

    /// <summary>Points Windows at the file to start: this copy of Flint.</summary>
    [RelayCommand]
    private void StartThisCopy() => SetStartWithWindows(true);

    /// <summary>Gives the section Flint's entry in Windows' sign-in list.</summary>
    /// <param name="entry">The entry, or null when this copy cannot have one; starting with Windows is then not offered.</param>
    internal void UseSignIn(RunAtSignIn? entry)
    {
        signIn = entry;
        OnPropertyChanged(string.Empty);
    }

    private void SetStartWithWindows(bool value)
    {
        if (signIn is null)
        {
            return;
        }

        if (value)
        {
            signIn.Enable(settings.Current.General.StartInTray);
        }
        else
        {
            signIn.Disable();
        }

        settings.Update(current => current with { General = current.General with { StartWithWindows = value } });
        OnPropertyChanged(nameof(StartWithWindows));
        OnPropertyChanged(nameof(StaleStart));
    }
}
