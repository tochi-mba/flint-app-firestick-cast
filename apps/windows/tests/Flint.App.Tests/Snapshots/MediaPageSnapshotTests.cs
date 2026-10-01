using Avalonia;
using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core.Media;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests.Snapshots;

/// <summary>The Now Playing card in each state it can be in, held to approved images.</summary>
/// <remarks>
/// The card's clock is one the test holds still, so every time on the card is the same on every
/// run and on every machine.
/// </remarks>
public sealed class MediaPageSnapshotTests
{
    private readonly ManualTime clock = new();

    [AvaloniaFact]
    public async Task Playing()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver, PlaybackPhase.Playing);
        media.NowPlaying.VolumePercent = 40;

        Snapshot.Matches("media-page-playing", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task Paused()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver, PlaybackPhase.Paused);
        media.NowPlaying.VolumePercent = 40;

        Snapshot.Matches("media-page-paused", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task VolumeNotSetYet()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver, PlaybackPhase.Playing);

        Snapshot.Matches("media-page-volume-unset", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task Sending()
    {
        await using var receiver = new LoopbackReceiver();
        var media = SnapshotFixtures.Media(await PairedAsync(receiver, time: clock));
        media.NowPlaying.BeginSending("Holiday in Lisbon.mp4", isPicture: false);
        media.NowPlaying.ReportSendProgress(0.42);

        Snapshot.Matches("media-page-sending", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task Finished()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver, PlaybackPhase.Finished, position: 5_520_000);

        Snapshot.Matches("media-page-finished", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task Problem()
    {
        await using var receiver = new LoopbackReceiver();
        var media = SnapshotFixtures.Media(await PairedAsync(receiver, time: clock));
        media.NowPlaying.BeginSending("Holiday in Lisbon.mkv", isPicture: false);
        media.NowPlaying.ShowProblem("The TV cannot play this file: its video is in a format the TV does not decode.");

        Snapshot.Matches("media-page-problem", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task Picture()
    {
        await using var receiver = new LoopbackReceiver();
        var media = SnapshotFixtures.Media(await PairedAsync(receiver, time: clock));
        media.NowPlaying.BeginSending("Beach.jpg", isPicture: true);
        media.NowPlaying.SendFinished(new PlaybackSnapshot(PlaybackPhase.Playing, 0, -1, "", clock.GetUtcNow()));

        Snapshot.Matches("media-page-picture", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task ConnectionLost()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver, PlaybackPhase.Playing);
        media.NowPlaying.ConnectionLost("The connection to Living Room was lost.");

        Snapshot.Matches("media-page-connection-lost", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task Narrow()
    {
        await using var receiver = new LoopbackReceiver();
        var media = await PlayingAsync(receiver, PlaybackPhase.Playing);
        media.NowPlaying.VolumePercent = 40;

        Snapshot.Matches("media-page-narrow", Page(media), new PixelSize(700, 1000));
    }

    private static PixelSize Tall => new(1024, 1000);

    private static MediaPage Page(MediaPageViewModel media) => new() { DataContext = media };

    private async Task<MediaPageViewModel> PlayingAsync(
        LoopbackReceiver receiver,
        PlaybackPhase phase,
        long position = 760_000)
    {
        var media = SnapshotFixtures.Media(await PairedAsync(receiver, time: clock));
        media.NowPlaying.BeginSending("Holiday in Lisbon.mp4", isPicture: false);
        media.NowPlaying.SendFinished(new PlaybackSnapshot(phase, position, 5_520_000, "", clock.GetUtcNow()));
        return media;
    }
}
