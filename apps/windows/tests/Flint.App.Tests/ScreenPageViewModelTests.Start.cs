using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>Starting a share from the Screen page, and changing it while it runs.</summary>
public sealed partial class ScreenPageViewModelTests
{
    private readonly RecordingMirrorEngine engine = new();

    [AvaloniaFact]
    public async Task Sharing_CountsDown_ThenSharesTheChosenDisplay()
    {
        settings.Update(current => current with { Screen = current.Screen with { CountdownSeconds = 3 } });
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        screen.SelectedDisplay = Side;

        var sharing = screen.ShareCommand.ExecuteAsync(null);

        screen.IsCountingDown.ShouldBeTrue();
        screen.CanShare.ShouldBeFalse();
        screen.CountdownText.ShouldBe("SHARING IN 3…");
        clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => screen.CountdownText == "SHARING IN 2…");
        clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => screen.CountdownText == "SHARING IN 1…");
        engine.Started.ShouldBeEmpty("nothing is shared until the countdown ends");
        clock.Advance(TimeSpan.FromSeconds(1));
        await tv.WaitForAsync<VideoConfigMessage>();

        engine.Started.ShouldHaveSingleItem().OutputIndex.ShouldBe(1u);
        screen.IsCountingDown.ShouldBeFalse();
        screen.CountdownText.ShouldBeNull();
        await screen.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
    }

    [AvaloniaFact]
    public async Task ClosingThePage_DuringACountdown_SharesNothing()
    {
        settings.Update(current => current with { Screen = current.Screen with { CountdownSeconds = 5 } });
        await using var tv = new LoopbackReceiver();
        var screen = Page(await PairedAsync(tv, engine, clock));

        var sharing = screen.ShareCommand.ExecuteAsync(null);
        screen.Dispose();
        await sharing;

        engine.Started.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task ACancelledCountdown_SharesNothing()
    {
        settings.Update(current => current with { Screen = current.Screen with { CountdownSeconds = 5 } });
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));

        var sharing = screen.ShareCommand.ExecuteAsync(null);
        screen.CancelCountdownCommand.Execute(null);
        await sharing;

        engine.Started.ShouldBeEmpty();
        screen.IsCountingDown.ShouldBeFalse();
        screen.CanShare.ShouldBeTrue();
        screen.CancelCountdownCommand.Execute(null);
    }

    [AvaloniaFact]
    public async Task AskingEachTime_OpensTheChooser_AndSharesThePickedDisplay()
    {
        settings.Update(current => current with { Screen = current.Screen with { DisplayPrompt = ShareDisplayPrompt.AskEveryTime } });
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));

        await screen.ShareCommand.ExecuteAsync(null);
        screen.IsChoosingDisplay.ShouldBeTrue();
        engine.Started.ShouldBeEmpty();

        var sharing = screen.ShareDisplayCommand.ExecuteAsync(Side);
        await tv.WaitForAsync<VideoConfigMessage>();

        screen.IsChoosingDisplay.ShouldBeFalse();
        screen.SelectedDisplay.ShouldBe(Side);
        engine.Started.ShouldHaveSingleItem().OutputIndex.ShouldBe(1u);
        await screen.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
    }

    [Fact]
    public async Task ClosingTheChooser_OrPickingNothing_SharesNothing()
    {
        settings.Update(current => current with { Screen = current.Screen with { DisplayPrompt = ShareDisplayPrompt.AskEveryTime } });
        using var screen = Page();

        await screen.ShareCommand.ExecuteAsync(null);
        screen.CancelChoosingCommand.Execute(null);
        screen.IsChoosingDisplay.ShouldBeFalse();

        await screen.ShareDisplayCommand.ExecuteAsync(null);
        screen.IsChoosingDisplay.ShouldBeFalse();
        screen.SelectedDisplay.ShouldBe(Main);
    }

    [Fact]
    public async Task AskingEachTime_WithOneDisplay_DoesNotAsk()
    {
        settings.Update(current => current with { Screen = current.Screen with { DisplayPrompt = ShareDisplayPrompt.AskEveryTime } });
        displays.Displays = [Main];
        using var screen = Page();

        await screen.ShareCommand.ExecuteAsync(null);

        screen.IsChoosingDisplay.ShouldBeFalse();
        screen.Cast.Failure.ShouldNotBeNull("it went straight to sharing, which an unprobed page refuses");
    }

    [AvaloniaFact]
    public async Task ChangingTheDisplayOrPicture_WhileSharing_SwitchesInPlace()
    {
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedAsync(tv, engine, clock));
        var sharing = screen.ShareCommand.ExecuteAsync(null);
        await tv.WaitForAsync<VideoConfigMessage>();
        screen.CanShare.ShouldBeFalse("a share is running");

        screen.SelectedDisplay = Side;
        screen.IsSwitching.ShouldBeTrue();
        screen.SwitchStatus.ShouldBe("Switching…");
        await Until(() => !screen.IsSwitching);
        screen.SwitchStatus.ShouldBeNull();

        screen.ChosenMode = screen.PictureModes.Single(choice => choice.Mode is PictureMode.Game);
        await Until(() => engine.Started.Count == 3 && !screen.IsSwitching);

        engine.Started[1].OutputIndex.ShouldBe(1u);
        engine.Started[2].FrameRate.ShouldBe(60u);
        screen.LivePictureSize.ShouldBe("1280 × 720");
        screen.LiveEncoder.ShouldBe("Graphics card");
        tv.Received.OfType<SurfaceMessage>().Count(surface => surface.Mode is SurfaceMode.Mirror).ShouldBe(1);

        await tv.SendAsync(new StatsMessage(3, 0, 0, 4));
        await Until(() => screen.LiveDroppedFrames == "4");
        screen.LiveTvQueue.ShouldBe("3");

        await screen.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
        await Until(() => screen.LivePictureSize == ScreenPageViewModel.NoFigure);
        screen.IsSwitching.ShouldBeFalse();
        screen.CanShare.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task TheLog_SaysWhichDisplayByNumber_NeverByNameOrPath()
    {
        var secret = new DisplayInfoBuilder("SECRET-MONITOR", @"\\?\DISPLAY#SECRET#PATH").Build();
        displays.Displays = [Main, secret];
        await using var tv = new LoopbackReceiver { Name = "SECRET-TV" };
        using var screen = Page(await PairedAsync(tv, engine, clock));
        using var capture = new LogCapture();

        var sharing = screen.ShareCommand.ExecuteAsync(null);
        await tv.WaitForAsync<VideoConfigMessage>();
        screen.SelectedDisplay = secret;
        await Until(() => !screen.IsSwitching);
        await screen.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;

        capture.Lines.ShouldContain(line => line.Contains("mirror begin display=0", StringComparison.Ordinal));
        capture.Lines.ShouldContain(line => line.Contains("mirror change display=5", StringComparison.Ordinal));
        capture.Lines.ShouldAllBe(line => !line.Contains("SECRET", StringComparison.Ordinal) && !line.Contains("123456", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePage_LetsGoOfTheCastPageAndSettings_WhenDisposed()
    {
        var cast = SnapshotFixturesViewModel();
        var screen = Page(cast);
        screen.Dispose();
        var raised = 0;
        screen.PropertyChanged += (_, _) => raised++;

        settings.Update(current => current with { Screen = current.Screen with { PictureMode = PictureMode.Movie } });

        raised.ShouldBe(0);
    }

    private static CastPageViewModel SnapshotFixturesViewModel() => Snapshots.SnapshotFixtures.ViewModel();

    /// <summary>A display whose name and path must never reach the log.</summary>
    private sealed record DisplayInfoBuilder(string Name, string Identity)
    {
        public DisplayInfo Build() => new(5, 2, Name, Identity, 1920, 0, 1920, 1080, DisplayRotation.Upright, IsMain: false);
    }

    /// <summary>Collects every trace line written while it is open.</summary>
    private sealed class LogCapture : System.Diagnostics.TraceListener
    {
        public LogCapture() => System.Diagnostics.Trace.Listeners.Add(this);

        public List<string> Lines { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                lock (Lines)
                {
                    Lines.Add(message);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            System.Diagnostics.Trace.Listeners.Remove(this);
            base.Dispose(disposing);
        }
    }
}
