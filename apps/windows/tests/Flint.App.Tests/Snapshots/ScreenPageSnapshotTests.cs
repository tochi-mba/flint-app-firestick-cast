using Avalonia;
using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Flint.Session;

namespace Flint.App.Tests.Snapshots;

/// <summary>The Screen page with its display map, picture modes and live numbers, held to approved images.</summary>
public sealed class ScreenPageSnapshotTests : IDisposable
{
    private static readonly PixelSize Tall = new(1280, 1200);

    private static readonly DisplayInfo Side =
        new(1, 2, "Display 2", "side", 2560, 160, 1920, 1080, DisplayRotation.Upright, IsMain: false);

    private static readonly DisplayInfo Portrait =
        new(2, 3, "Built-in display", "portrait", -1080, 0, 1080, 1920, DisplayRotation.QuarterClockwise, IsMain: false);

    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly ManualTime clock = new();

    public void Dispose() => settings.Dispose();

    [AvaloniaFact]
    public async Task TwoDisplays()
    {
        using var screen = await PageAsync(SnapshotFixtures.MainDisplay, Side);

        Snapshot.Matches("screen-page-two-displays", Page(screen), Tall);
    }

    [AvaloniaFact]
    public async Task ThreeDisplaysArranged()
    {
        using var screen = await PageAsync(Portrait, SnapshotFixtures.MainDisplay, Side);
        screen.SelectedDisplay = Side;

        Snapshot.Matches("screen-page-three-displays-arranged", Page(screen), Tall);
    }

    [AvaloniaFact]
    public async Task RotatedDisplay()
    {
        using var screen = await PageAsync(Portrait, SnapshotFixtures.MainDisplay);
        screen.SelectedDisplay = Portrait;

        Snapshot.Matches("screen-page-rotated-display", Page(screen), Tall);
    }

    [AvaloniaFact]
    public async Task CustomMode()
    {
        settings.Update(current => current with
        {
            Screen = current.Screen with
            {
                PictureMode = PictureMode.Custom,
                CustomSizeLimit = SizeLimit.MatchTv,
                CustomFramesPerSecond = 24,
                CustomMegabitsPerSecond = 9,
            },
        });
        using var screen = await PageAsync(SnapshotFixtures.MainDisplay);

        Snapshot.Matches("screen-page-custom-mode", Page(screen), Tall);
    }

    [AvaloniaFact]
    public async Task LiveNumbers()
    {
        settings.Update(current => current with { Screen = current.Screen with { ShowLiveNumbers = true } });
        using var screen = await PageAsync(SnapshotFixtures.MainDisplay);
        screen.OnPictureStarted(new MirrorPicture(1920, 1080, MirrorEncoderKind.Hardware));
        screen.OnStats(new MirrorSessionStats(0, 0, 0, 0));
        clock.Advance(TimeSpan.FromSeconds(2));
        screen.OnStats(new MirrorSessionStats(60, 4, 1, 3_000_000));
        screen.OnReceiverStats(new StatsMessage(2, 0, 0, 3));

        Snapshot.Matches("screen-page-live-numbers", Page(screen), Tall);
    }

    [AvaloniaFact]
    public async Task Switching()
    {
        // A real share, so the page shows what it shows while sharing; the switch itself is held
        // at its first moment, because a real one finishes before the image could be taken.
        await using var tv = new LoopbackReceiver { Name = "Living Room" };
        var cast = await CastPageFixtures.PairedAsync(tv, new RecordingMirrorEngine(), clock);
        using var screen = new ScreenPageViewModel(cast, settings, new SnapshotFixtures.FixedDisplays([SnapshotFixtures.MainDisplay, Side]), clock);
        var sharing = screen.ShareCommand.ExecuteAsync(null);
        await tv.WaitForAsync<VideoConfigMessage>();
        screen.IsSwitching = true;
        screen.SwitchStatus = "Switching…";

        Snapshot.Matches("screen-page-switching", Page(screen), Tall);

        await cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
    }

    [AvaloniaFact]
    public async Task PausedHoldingThePicture()
    {
        using var screen = await PageAsync(SnapshotFixtures.MainDisplay);
        screen.Cast.IsMirroring = true;
        screen.Cast.MirrorPause = MirrorPause.HoldingLastPicture;
        clock.Advance(TimeSpan.FromSeconds(83));

        Snapshot.Matches("screen-page-paused-holding", Page(screen), Tall);
    }

    [AvaloniaFact]
    public async Task PausedOnABlackScreen()
    {
        using var screen = await PageAsync(SnapshotFixtures.MainDisplay);
        screen.Cast.IsMirroring = true;
        screen.Cast.MirrorPause = MirrorPause.Black;

        Snapshot.Matches("screen-page-paused-black", Page(screen), Tall);
    }

    [AvaloniaFact]
    public async Task Struggling()
    {
        using var screen = await PageAsync(SnapshotFixtures.MainDisplay);
        screen.ShowStrugglingSuggestion = true;

        Snapshot.Matches("screen-page-struggling", Page(screen), Tall);
    }

    private static ScreenPage Page(ScreenPageViewModel screen) => new() { DataContext = screen };

    private async Task<ScreenPageViewModel> PageAsync(params DisplayInfo[] displays) =>
        new(
            await SnapshotFixtures.ProbedViewModel(),
            settings,
            new SnapshotFixtures.FixedDisplays(displays),
            clock);
}
