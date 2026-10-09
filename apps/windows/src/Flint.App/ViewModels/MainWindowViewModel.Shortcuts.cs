using System.Windows.Input;
using Avalonia.Input;
using Flint.App.Services;
using Flint.App.ViewModels.Settings;

namespace Flint.App.ViewModels;

/// <summary>What each shortcut does, through the same commands the pages use.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>How far one volume shortcut moves the TV while sharing, in percent.</summary>
    internal const int ShortcutVolumeStep = 10;

    private HotKeyService? hotKeys;

    /// <summary>Hands the shell the service that claims shortcuts, for Settings and for keys pressed in the window.</summary>
    internal void UseShortcuts(HotKeyService service)
    {
        hotKeys = service ?? throw new ArgumentNullException(nameof(service));
        ShortcutsSection.UseService(service);
    }

    /// <summary>A key pressed in Flint's window: a shortcut's action, when shortcuts work only here.</summary>
    /// <returns>Whether it was a shortcut.</returns>
    internal bool OnWindowKey(Key key, KeyModifiers modifiers) =>
        hotKeys is not null
        && !ShortcutsSection.IsCapturing
        && ShortcutKeys.Of(key, modifiers) is { } pressed
        && hotKeys.OnWindowKey(pressed);

    private ShortcutsSettingsViewModel ShortcutsSection => Settings.Sections.OfType<ShortcutsSettingsViewModel>().Single();

    /// <summary>Runs a shortcut's action.</summary>
    /// <returns>Whether it did anything; a shortcut that cannot act now does nothing at all.</returns>
    /// <remarks>
    /// Starting a share goes through the Screen page's own command, so the usual question is asked
    /// when the TV shows something else, and the window comes forward to ask it.
    /// </remarks>
    internal bool RunShortcut(ShortcutAction action)
    {
        if (action is ShortcutAction.ShowWindow)
        {
            ShowWindow();
            return true;
        }

        if (action is ShortcutAction.VolumeUp or ShortcutAction.VolumeDown && !Media.NowPlaying.IsActive)
        {
            return NudgeSharedVolume(action is ShortcutAction.VolumeUp ? ShortcutVolumeStep : -ShortcutVolumeStep);
        }

        ICommand command = action switch
        {
            ShortcutAction.StartStopSharing => Cast.IsMirroring ? Cast.StopScreenSessionCommand : Screen.ShareCommand,
            ShortcutAction.PauseResumeSharing => Cast.IsMirrorPaused ? Screen.ResumeCommand : Screen.PauseCommand,
            ShortcutAction.PlayPauseTv => Media.NowPlaying.PlayPauseCommand,
            ShortcutAction.NextItem => Media.Queue.NextCommand,
            ShortcutAction.PreviousItem => Media.Queue.PreviousCommand,
            ShortcutAction.VolumeUp => Media.NowPlaying.VolumeUpCommand,
            ShortcutAction.VolumeDown => Media.NowPlaying.VolumeDownCommand,
            _ => Cast.DisconnectCommand,
        };
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    /// <summary>Moves the TV's volume while sharing, when nothing is playing to take a volume step.</summary>
    private bool NudgeSharedVolume(int by)
    {
        if (!Screen.CanSetTvVolume)
        {
            return false;
        }

        Screen.TvVolume += by;
        return true;
    }
}
