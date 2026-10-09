using System.Net;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core.Shortcuts;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>What each shortcut does to the shell, and keys pressed in Flint's own window.</summary>
public sealed class ShellShortcutTests
{
    private readonly MainWindowViewModel shell = MainWindowViewModel.CreateWith(
        BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }),
        addressStore: new NoRecentAddresses(),
        mirrorEngine: new RecordingMirrorEngine());

    private readonly RecordingWindow window = new();

    public ShellShortcutTests() => shell.UseWindow(window);

    [AvaloniaFact]
    public void WithNothingConnected_EveryShortcutButShowWindowDoesNothing()
    {
        foreach (var action in Enum.GetValues<ShortcutAction>().Where(action => action is not ShortcutAction.ShowWindow))
        {
            shell.RunShortcut(action).ShouldBeFalse(action.ToString());
        }

        shell.RunShortcut(ShortcutAction.ShowWindow).ShouldBeTrue();
        window.Shown.ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task WhileSharing_TheShortcutsPauseResumeTurnUpAndStop()
    {
        await using var tv = new LoopbackReceiver();
        await Pair(shell.Cast, tv);
        var sharing = shell.Cast.StartMirrorAsync(shell.Cast.MirrorOptionsFor(null));
        await tv.WaitForAsync<VideoConfigMessage>();

        shell.RunShortcut(ShortcutAction.PauseResumeSharing).ShouldBeTrue();
        shell.Cast.IsMirrorPaused.ShouldBeTrue();
        shell.RunShortcut(ShortcutAction.PauseResumeSharing).ShouldBeTrue();
        shell.Cast.IsMirrorPaused.ShouldBeFalse();

        shell.RunShortcut(ShortcutAction.VolumeDown).ShouldBeTrue();
        shell.Screen.TvVolume.ShouldBe(90);
        shell.RunShortcut(ShortcutAction.VolumeUp).ShouldBeTrue();
        shell.Screen.TvVolume.ShouldBe(100);

        shell.RunShortcut(ShortcutAction.StartStopSharing).ShouldBeTrue();
        await sharing;
        shell.Cast.IsMirroring.ShouldBeFalse();

        shell.RunShortcut(ShortcutAction.Disconnect).ShouldBeTrue();
        await Until(() => !shell.Cast.IsSessionConnected);
    }

    [AvaloniaFact]
    public async Task WhilePlaying_TheVolumeShortcutsAreThePlayersOwn()
    {
        await using var tv = new LoopbackReceiver();
        await Pair(shell.Cast, tv);
        shell.Media.NowPlaying.BeginSending("Film.mp4", isPicture: false);

        shell.RunShortcut(ShortcutAction.VolumeUp).ShouldBeFalse("no volume set from here yet, so no step to take");
        shell.RunShortcut(ShortcutAction.VolumeDown).ShouldBeFalse();
        shell.Screen.TvVolume.ShouldBe(100, "the shared-sound slider is left alone");
    }

    [AvaloniaFact]
    public void KeysInTheWindow_RunShortcuts_OnlyWhenTheyWorkOnlyThere_AndNotWhileARowWaits()
    {
        shell.OnWindowKey(Key.F, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift).ShouldBeFalse("no service yet");
        using var service = new HotKeyService(new NoRegistrar(), shell.SettingsService, shell.RunShortcut);
        shell.UseShortcuts(service);
        shell.OnWindowKey(Key.F, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift).ShouldBeFalse("they work everywhere, through Windows");

        shell.SettingsService.Update(current => current with { Shortcuts = current.Shortcuts with { WorkInBackground = false } });

        shell.OnWindowKey(Key.F, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift).ShouldBeTrue();
        window.Shown.ShouldBe(1);
        shell.OnWindowKey(Key.LeftCtrl, KeyModifiers.Control).ShouldBeFalse();

        var section = shell.Settings.Sections.OfType<Flint.App.ViewModels.Settings.ShortcutsSettingsViewModel>().Single();
        section.Rows[0].ChangeCommand.Execute(null);
        shell.OnWindowKey(Key.F, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift).ShouldBeFalse("the key is the row's new shortcut");
        Should.Throw<ArgumentNullException>(() => shell.UseShortcuts(null!));
    }

    [AvaloniaFact]
    public void AClaimedShortcut_PressedAnywhere_ReachesTheShell()
    {
        // Only one unusual combination is claimed, so the person's own shortcuts are never taken.
        shell.SettingsService.Update(current => current with
        {
            Shortcuts = new Flint.Core.Settings.ShortcutSettings
            {
                StartStopSharing = null,
                PauseResumeSharing = null,
                PlayPauseTv = null,
                NextItem = null,
                PreviousItem = null,
                VolumeUp = null,
                VolumeDown = null,
                ShowWindow = "Ctrl+Alt+Shift+F23",
            },
        });
        using var shortcuts = GlobalShortcuts.Start(shell).ShouldNotBeNull();

        shortcuts.Window.Send(Flint.Platform.Windows.GlobalHotKeys.HotKeyMessage, (int)ShortcutAction.ShowWindow, 0);

        window.Shown.ShouldBe(1);
        Should.Throw<ArgumentNullException>(() => GlobalShortcuts.Start(null!));
    }

    private sealed class NoRegistrar : IHotKeyRegistrar
    {
        public event Action<int>? Pressed
        {
            add { }
            remove { }
        }

        public bool Register(int id, HotKeyModifiers modifiers, int virtualKey) => true;

        public void Unregister(int id)
        {
        }
    }
}
