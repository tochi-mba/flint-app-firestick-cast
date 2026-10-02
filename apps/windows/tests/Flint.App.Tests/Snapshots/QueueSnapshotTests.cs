using System.Reflection;
using Avalonia;
using Avalonia.Headless.XUnit;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core.Media;
using Flint.Protocol;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests.Snapshots;

/// <summary>The Up next list, the resume question and a waiting file, held to approved images.</summary>
/// <remarks>Real files in a folder of their own, so the list shows real names; only the names show.</remarks>
public sealed class QueueSnapshotTests : IDisposable
{
    private readonly ManualTime clock = new();
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"flint-snap-{Guid.NewGuid():N}");

    public QueueSnapshotTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [AvaloniaFact]
    public async Task UpNext()
    {
        await using var tv = new LoopbackReceiver { AnswerLoad = LoopbackReceiver.PlaysEverything };
        var media = SnapshotFixtures.Media(await PairedAsync(tv, time: clock));

        await media.Queue.AddDroppedAsync([Make("Episode 1.mkv"), Make("Episode 2.mkv"), Make("Episode 10.mkv"), Make("Theme.flac")]);
        await Until(() => media.NowPlaying.IsActive && !media.NowPlaying.IsSending);

        Snapshot.Matches("media-page-queue", Page(media), Tall);
    }

    [AvaloniaFact]
    public async Task ResumeQuestion()
    {
        await using var tv = new LoopbackReceiver { AnswerLoad = LoopbackReceiver.PlaysEverything };
        var history = new InMemoryMediaHistoryStore();
        var media = SnapshotFixtures.Media(await PairedAsync(tv, time: clock), history: history);
        var film = Make("Holiday in Lisbon.mp4");
        var facts = new FileInfo(film);
        history.Save(new MediaHistoryEntry(
            ResumePolicy.KeyFor(film, facts.Length, facts.LastWriteTimeUtc), 2_467_000, 5_520_000, clock.GetUtcNow()));

        var adding = media.Queue.AddDroppedAsync([film]);
        await Until(() => media.Queue.IsAskingResume);

        Snapshot.Matches("media-page-resume-question", Page(media), Tall);
        media.Queue.StartOverCommand.Execute(null);
        await adding;
    }

    [AvaloniaFact]
    public async Task WaitingForTheTv()
    {
        await using var tv = new LoopbackReceiver { AnswerLoad = LoopbackReceiver.PlaysEverything };
        var cast = await PairedAsync(tv, time: clock);
        _ = new ModeSessionCoordinator(cast, new BrowserPageViewModel(cast));
        var media = SnapshotFixtures.Media(cast);
        await media.Queue.AddDroppedAsync([Make("Episode 1.mkv"), Make("Episode 2.mkv")]);
        await Until(() => media.NowPlaying.IsActive && !media.NowPlaying.IsSending);
        typeof(CastPageViewModel).GetField("_isMirroring", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cast, true);

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Ended, 90_000, 90_000));
        await Until(() => media.Queue.IsWaiting);

        Snapshot.Matches("media-page-queue-waiting", Page(media), Tall);
    }

    private static PixelSize Tall => new(1024, 1300);

    private static MediaPage Page(MediaPageViewModel media) => new() { DataContext = media };

    private string Make(string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[64]);
        return path;
    }
}
