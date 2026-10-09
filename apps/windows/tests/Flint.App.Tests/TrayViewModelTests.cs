using System.Net;
using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.Core;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>The tray icon's look, tooltip and menu in each state, and what its choices do.</summary>
public sealed class TrayViewModelTests
{
    private readonly ManualTime clock = new();

    [AvaloniaFact]
    public void NotConnected_OffersToOpenShareSettingsAndQuit()
    {
        var (shell, _) = Shell();
        using var tray = new TrayViewModel(shell, clock);

        tray.State.ShouldBe(TrayState.Idle);
        tray.Tooltip.ShouldBe("Flint: not connected to a TV");
        Labels(tray).ShouldBe(["Not connected to a TV", "Open Flint", "Share screen", "Settings", "Quit Flint"]);
        tray.Menu[0].IsEnabled.ShouldBeFalse("the status line only says something");
        tray.Menu[2].IsEnabled.ShouldBeFalse("nothing to share to yet");
    }

    [AvaloniaFact]
    public async Task Connected_ThenSharing_ThenPaused_EachSayWhatIsHappening()
    {
        await using var tv = new LoopbackReceiver();
        var (shell, window) = Shell();
        await Pair(shell.Cast, tv);
        using var tray = new TrayViewModel(shell, clock);

        tray.State.ShouldBe(TrayState.Connected);
        tray.Tooltip.ShouldBe("Flint: connected to Living Room");
        Labels(tray).ShouldBe(["Connected to Living Room", "Open Flint", "Share screen", "Disconnect", "Settings", "Quit Flint"]);

        shell.Cast.IsMirroring = true;
        tray.State.ShouldBe(TrayState.Sharing);
        tray.Tooltip.ShouldBe("Flint: sharing your screen to Living Room");
        Labels(tray).ShouldBe(["Sharing your screen to Living Room", "Open Flint", "Stop sharing", "Pause sharing", "Disconnect", "Settings", "Quit Flint"]);

        shell.Cast.MirrorPause = MirrorPause.HoldingLastPicture;
        tray.State.ShouldBe(TrayState.Paused);
        tray.Tooltip.ShouldBe("Flint: sharing to Living Room, paused");
        Labels(tray).ShouldContain("Resume sharing");
        Labels(tray).ShouldNotContain("Pause sharing");

        shell.Cast.MirrorPause = MirrorPause.Running;
        shell.Cast.IsMirroring = false;
        window.Shown.ShouldBe(0);
    }

    [AvaloniaFact]
    public async Task APlayingFile_AddsPlayOrPauseAndNext()
    {
        await using var tv = new LoopbackReceiver();
        var (shell, _) = Shell();
        await Pair(shell.Cast, tv);
        using var tray = new TrayViewModel(shell, clock);

        shell.Media.NowPlaying.BeginSending("Film.mp4", isPicture: false);

        tray.Tooltip.ShouldBe("Flint: playing on Living Room");
        Labels(tray).ShouldBe(["Playing on Living Room", "Open Flint", "Share screen", "Pause", "Next", "Disconnect", "Settings", "Quit Flint"]);
    }

    [AvaloniaFact]
    public void Reconnecting_IsSaid()
    {
        var (shell, _) = Shell();
        using var tray = new TrayViewModel(shell, clock);

        shell.Cast.IsReconnecting = true;

        tray.Tooltip.ShouldBe("Flint: reconnecting to the TV");
        tray.State.ShouldBe(TrayState.Idle);
        shell.Cast.IsReconnecting = false;
    }

    [AvaloniaFact]
    public void TheMenu_IsOnlyRebuiltWhenWhatItShowsChanges()
    {
        var (shell, _) = Shell();
        using var tray = new TrayViewModel(shell, clock);
        var built = tray.Menu;
        var changes = 0;
        tray.PropertyChanged += (_, change) => changes += change.PropertyName == nameof(TrayViewModel.Menu) ? 1 : 0;

        shell.Cast.MirrorStatus = "Mirroring: 3 frames";
        shell.Cast.Failure = "Something";
        shell.Cast.IsMirroring = false;

        changes.ShouldBe(0);
        tray.Menu.ShouldBeSameAs(built);
    }

    [AvaloniaFact]
    public void OpenSettingsAndQuit_DoWhatTheySay()
    {
        var (shell, window) = Shell();
        using var tray = new TrayViewModel(shell, clock);

        Item(tray, "Open Flint").Command!.Execute(null);
        window.Shown.ShouldBe(1);
        Item(tray, "Settings").Command!.Execute(null);
        shell.Selected.Label.ShouldBe("Settings");
        window.Shown.ShouldBe(2);
        Item(tray, "Quit Flint").Command!.Execute(null);
        window.ShutDown.ShouldBe(1, "nothing is on the TV, so Flint quits without asking");
    }

    [AvaloniaFact]
    public void OneClick_OpensFlint_OrTwoWhenTheSettingSays()
    {
        var (shell, window) = Shell();
        using var tray = new TrayViewModel(shell, clock);

        tray.OnClicked();
        window.Shown.ShouldBe(1);

        shell.SettingsService.Update(current => current with { Tray = current.Tray with { SingleClickOpens = false } });
        tray.OnClicked();
        window.Shown.ShouldBe(1, "one click does nothing");
        clock.Advance(TrayViewModel.DoubleClick + TimeSpan.FromMilliseconds(1));
        tray.OnClicked();
        window.Shown.ShouldBe(1, "too far apart to be a double click");
        clock.Advance(TrayViewModel.DoubleClick);
        tray.OnClicked();
        window.Shown.ShouldBe(2);
        tray.OnClicked();
        window.Shown.ShouldBe(2, "a third click starts again");
    }

    [Fact]
    public void ATrayNeedsAShell()
    {
        Should.Throw<ArgumentNullException>(() => new TrayViewModel(null!));
    }

    private static List<string> Labels(TrayViewModel tray) => [.. tray.Menu.Select(item => item.Label)];

    private static TrayMenuItem Item(TrayViewModel tray, string label) => tray.Menu.Single(item => item.Label == label);

    private static (MainWindowViewModel Shell, RecordingWindow Window) Shell()
    {
        var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }),
            addressStore: new NoRecentAddresses());
        var window = new RecordingWindow();
        shell.UseWindow(window);
        return (shell, window);
    }
}

/// <summary>A window that counts what the shell asked of it.</summary>
internal sealed class RecordingWindow : IWindowControl
{
    public int Shown { get; private set; }

    public int Hidden { get; private set; }

    public int ShutDown { get; private set; }

    public void Show() => Shown++;

    public void Hide() => Hidden++;

    public void Shutdown() => ShutDown++;
}
