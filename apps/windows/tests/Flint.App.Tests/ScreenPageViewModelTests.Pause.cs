using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>Pausing a share by hand and when this PC locks, and what the page says while paused.</summary>
public sealed partial class ScreenPageViewModelTests
{
    [AvaloniaFact]
    public async Task Pausing_HoldsTheLastPicture_AndCountsTheTime_UntilResume()
    {
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        var sharing = await SharingAsync(screen, tv);
        screen.CanPause.ShouldBeTrue();

        screen.PauseCommand.Execute(null);

        screen.IsPaused.ShouldBeTrue();
        screen.CanPause.ShouldBeFalse();
        screen.Cast.MirrorPause.ShouldBe(MirrorPause.HoldingLastPicture);
        screen.PausedBanner.ShouldBe("Paused. The TV is holding the last picture.");
        screen.PausedFor.ShouldBe("Paused for 0:00");
        clock.Advance(TimeSpan.FromSeconds(65));
        await Until(() => screen.PausedFor == "Paused for 1:05");
        clock.Advance(TimeSpan.FromHours(1));
        await Until(() => screen.PausedFor == "Paused for 1:01:05");

        screen.ResumeCommand.Execute(null);

        screen.IsPaused.ShouldBeFalse();
        screen.PausedBanner.ShouldBeNull();
        screen.PausedFor.ShouldBeNull();
        screen.Cast.MirrorPause.ShouldBe(MirrorPause.Running);
        await StopAsync(screen, sharing);
    }

    [AvaloniaFact]
    public async Task Pausing_WithABlackScreen_SaysSo()
    {
        settings.Update(current => current with { Screen = current.Screen with { PausedPicture = PausedPicture.Black } });
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        var sharing = await SharingAsync(screen, tv);

        screen.PauseCommand.Execute(null);

        screen.Cast.MirrorPause.ShouldBe(MirrorPause.Black);
        screen.PausedBanner.ShouldBe("Paused. The TV is showing a black screen.");
        await StopAsync(screen, sharing);
        screen.IsPaused.ShouldBeFalse("a pause ends with its share");
        screen.PausedFor.ShouldBeNull();
    }

    [AvaloniaFact]
    public async Task Locking_PausesAsTheSettingsSay_AndUnlockingResumesOrWaits()
    {
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        var sharing = await SharingAsync(screen, tv);

        screen.OnPcLocked();
        screen.IsPaused.ShouldBeTrue("pausing on lock is on by default");
        screen.OnPcUnlocked();
        screen.IsPaused.ShouldBeFalse("and resuming on unlock is the default too");

        settings.Update(current => current with { Screen = current.Screen with { StayPausedAfterUnlock = true } });
        screen.OnPcLocked();
        screen.OnPcUnlocked();
        screen.IsPaused.ShouldBeTrue("the setting says to wait for RESUME");
        screen.ResumeCommand.Execute(null);

        settings.Update(current => current with { Screen = current.Screen with { PauseWhenLocked = false } });
        screen.OnPcLocked();
        screen.IsPaused.ShouldBeFalse();
        await StopAsync(screen, sharing);
    }

    [AvaloniaFact]
    public async Task APauseThePersonMade_IsNotUndoneByUnlocking()
    {
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        var sharing = await SharingAsync(screen, tv);

        screen.PauseCommand.Execute(null);
        screen.OnPcLocked();
        screen.OnPcUnlocked();

        screen.IsPaused.ShouldBeTrue();
        await StopAsync(screen, sharing);
    }

    [Fact]
    public void Locking_WithNothingShared_ChangesNothing()
    {
        using var screen = Page();

        screen.OnPcLocked();
        screen.OnPcUnlocked();

        screen.IsPaused.ShouldBeFalse();
        screen.Cast.PauseMirror(MirrorPause.Black).ShouldBeFalse();
        screen.Cast.ResumeMirror().ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task AChangeWhilePaused_SaysItWaits_AndIsMadeOnResume()
    {
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        var sharing = await SharingAsync(screen, tv);
        screen.PauseCommand.Execute(null);

        screen.SelectedDisplay = Side;

        screen.SwitchStatus.ShouldBe("This change applies when you resume.");
        screen.IsSwitching.ShouldBeFalse();
        screen.Cast.PauseMirror(MirrorPause.Running).ShouldBeFalse("running is not a pause");
        screen.ResumeCommand.Execute(null);
        await Until(() => engine.Started.Count == 2);
        await Until(() => screen.SwitchStatus is null);
        engine.Started[1].OutputIndex.ShouldBe(1u);
        await StopAsync(screen, sharing);
    }

    [AvaloniaFact]
    public async Task TheTvGoingAway_WhilePaused_ForgetsThePause()
    {
        var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        var sharing = await SharingAsync(screen, tv);
        screen.PauseCommand.Execute(null);

        await tv.CloseAsync();
        await tv.DisposeAsync();
        await sharing;
        await Until(() => !screen.Cast.IsMirroring);

        screen.IsPaused.ShouldBeFalse();
        screen.PausedFor.ShouldBeNull();
        screen.Cast.MirrorPause.ShouldBe(MirrorPause.Running);
    }

    [Fact]
    public void ThePauseState_FollowsTheCastPage_AndItsTimerRunsWithoutAWindow()
    {
        // No window, so no dispatcher: the timer's tick runs where it fires.
        using var screen = Page();
        screen.Cast.IsMirroring = true;

        screen.OnPcLocked();
        screen.IsPaused.ShouldBeFalse("a share this page did not start cannot be paused from it");

        screen.Cast.MirrorPause = MirrorPause.HoldingLastPicture;
        screen.Cast.MirrorPause = MirrorPause.Black;
        screen.PausedBanner.ShouldBe("Paused. The TV is showing a black screen.");
        clock.Advance(TimeSpan.FromSeconds(3));
        screen.PausedFor.ShouldBe("Paused for 0:03");

        screen.Cast.MirrorPause = MirrorPause.Running;
        screen.ShowTimePaused();
        screen.PausedFor.ShouldBeNull("a tick that arrives after resuming changes nothing");
        clock.ActiveTimers.ShouldBe(0);
        screen.Cast.IsMirroring = false;
    }

    private static async Task<Task> SharingAsync(ScreenPageViewModel screen, LoopbackReceiver tv)
    {
        var sharing = screen.ShareCommand.ExecuteAsync(null);
        await tv.WaitForAsync<VideoConfigMessage>();
        return sharing;
    }

    private static async Task StopAsync(ScreenPageViewModel screen, Task sharing)
    {
        await screen.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
    }
}
