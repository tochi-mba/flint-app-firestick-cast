using System.Reflection;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>
/// The Up next list against a paired loopback TV: what starts, what waits, what moves on, and
/// what is remembered.
/// </summary>
public sealed partial class MediaQueueViewModelTests : IDisposable
{
    private readonly ManualTime clock = new();
    private readonly TempFiles files = new();
    private readonly InMemoryMediaHistoryStore history = new();
    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());

    public void Dispose()
    {
        files.Dispose();
        settings.Dispose();
    }

    [Fact]
    public async Task AddingWhileNothingPlays_StartsTheFirst_AndQueuesTheRest()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        var b = files.Make("b.mp3");

        await queue.AddDroppedAsync([b, a]);

        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].Title.ShouldBe("a.mp4");
        tv.Loads[0].MimeType.ShouldBe("video/mp4");
        queue.Rows.Select(row => row.Name).ShouldBe(["a.mp4", "b.mp3"]);
        queue.Rows[0].IsCurrent.ShouldBeTrue();
        queue.Rows[0].SpokenName.ShouldBe("a.mp4, playing");
        queue.Rows[1].KindLabel.ShouldBe("MUSIC");
        queue.HasItems.ShouldBeTrue();
        queue.Status.ShouldBe("Added 2 to the queue.");
    }

    [Fact]
    public async Task AddingWhileSomethingPlays_QueuesWithoutInterrupting()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await queue.AddDroppedAsync([files.Make("b.mp4")]);

        tv.Loads.Count.ShouldBe(1);
        queue.Status.ShouldBe("Added b.mp4 to the queue.");
        queue.Rows.Count.ShouldBe(2);
    }

    [Fact]
    public async Task NothingPlayable_AddsNothing_AndSaysWhy()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);

        await queue.AddDroppedAsync([files.Make("notes.txt")]);
        queue.Status.ShouldBe("None of those are files Flint can play.");

        await queue.AddAsync(new DropContents([], Capped: false, Skipped: 2));
        queue.Status.ShouldBe("Those folders could not be opened, so nothing was added.");
        queue.HasItems.ShouldBeFalse();
    }

    [Fact]
    public async Task ADropThatHitTheCapOrSkippedFolders_SaysSo()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");

        await queue.AddAsync(new DropContents([a], Capped: true, Skipped: 1));
        queue.Status.ShouldBe($"Added the first {DropExpander.MaximumFiles} files to the queue. 1 folder was not readable.");

        await queue.AddAsync(new DropContents([files.Make("b.mp4")], Capped: false, Skipped: 3));
        queue.Status.ShouldBe("Added b.mp4 to the queue. 3 folders were not readable.");
    }

    [Fact]
    public async Task AddingWithNoTv_QueuesAndSaysToConnect()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        using var queue = new MediaQueueViewModel(cast, settings, history, new LocalMediaFileSystem(), clock);

        await queue.AddDroppedAsync([files.Make("a.mp4")]);

        queue.HasItems.ShouldBeTrue();
        queue.Status.ShouldBe("Added a.mp4 to the queue. Connect to your TV on the Cast page to play them.");
        queue.NextCommand.CanExecute(null).ShouldBeFalse();
        queue.PreviousCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAFileEnds_TheNextOneIsSent()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await EndAsync(tv);

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("b.mp4");
        await Until(() => queue.Rows[1].IsCurrent);
    }

    [Fact]
    public async Task TheTvSayingEndedAgain_DoesNotMoveOnTwice()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4"), files.Make("c.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        tv.AnswerLoad = null;

        await EndAsync(tv);
        await Until(() => tv.Loads.Count == 2);
        await EndAsync(tv);
        await EndAsync(tv);

        tv.Loads.Count.ShouldBe(2, "the second file is still being sent, so its end has not come");
    }

    [Fact]
    public async Task WithAutoPlayOff_TheQueueWaitsForThePerson()
    {
        settings.Update(current => current with { Media = current.Media with { AutoPlayNext = false } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await EndAsync(tv);
        await Settle();

        tv.Loads.Count.ShouldBe(1);
        queue.NextCommand.CanExecute(null).ShouldBeTrue();
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2);
    }

    [Fact]
    public async Task AtTheEnd_TheLastItemStaysUp_OrTheTvGoesHome()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await EndAsync(tv);
        await Until(() => queue.Status == "The queue has finished.");
        Clears(tv).ShouldBe(0);

        settings.Update(current => current with { Media = current.Media with { QueueEnd = QueueEnd.TvHome } });
        await queue.PlayNowCommand.ExecuteAsync(queue.Rows[0]);
        await Until(() => tv.Loads.Count == 2);
        await EndAsync(tv);
        await Until(() => Clears(tv) == 1);
    }

    [Fact]
    public async Task RepeatOne_PlaysTheSameFileAgainWithoutSendingIt()
    {
        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.One, AutoPlayNext = false } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await EndAsync(tv);

        await Until(() => Transports(tv).Contains(TransportAction.Play));
        Transports(tv).ShouldBe([TransportAction.SeekTo, TransportAction.Play]);
        tv.Loads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RepeatOne_ReplaysOncePerEnd_ThoughTheTvRepeatsIt()
    {
        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.One } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await EndAsync(tv);
        await EndAsync(tv);
        await EndAsync(tv);
        await Until(() => Transports(tv).Count >= 2);
        await Settle();
        Transports(tv).ShouldBe([TransportAction.SeekTo, TransportAction.Play], "the TV said it again before it caught up");

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 0, 90_000));
        await EndAsync(tv);
        await Until(() => Transports(tv).Count >= 4);
        Transports(tv).ShouldBe([TransportAction.SeekTo, TransportAction.Play, TransportAction.SeekTo, TransportAction.Play]);
    }

    [Fact]
    public async Task RepeatAll_GoesBackToTheFirstFile()
    {
        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.All } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await EndAsync(tv);
        await Until(() => tv.Loads.Count == 2);
        await EndAsync(tv);

        await Until(() => tv.Loads.Count == 3);
        tv.Loads[2].Title.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task AFileThatHasGone_IsMarkedAndPassedOver()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var b = files.Make("b.mp4");
        await queue.AddDroppedAsync([files.Make("a.mp4"), b, files.Make("c.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        File.Delete(b);

        await EndAsync(tv);

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("c.mp4");
        queue.Rows[1].IsMissing.ShouldBeTrue();
        queue.Rows[1].Note.ShouldBe("File not found");
        queue.Rows[1].HasNote.ShouldBeTrue();
        queue.Status.ShouldBe("b.mp4 is no longer there, so it was skipped.");
    }

    [Fact]
    public async Task WhenEveryQueuedFileHasGone_RepeatingStopsInsteadOfGoingRoundForever()
    {
        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.All } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        var b = files.Make("b.mp4");
        await queue.AddAsync(new DropContents([a, b], false, 0));
        await Until(() => tv.Loads.Count == 1);
        File.Delete(a);
        File.Delete(b);

        await EndAsync(tv);

        await Until(() => queue.Status == "None of the queued files are there any more.");
        tv.Loads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AMissingLastFile_EndsTheQueue()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var b = files.Make("b.mp4");
        await queue.AddDroppedAsync([files.Make("a.mp4"), b]);
        await Until(() => tv.Loads.Count == 1);
        File.Delete(b);

        await EndAsync(tv);

        await Until(() => queue.Rows[1].IsMissing);
        queue.Status.ShouldBe("b.mp4 is no longer there, and nothing is queued after it.");
        tv.Loads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AFileTheTvCannotPlay_StopsTheQueue_AndOffersNext()
    {
        await using var tv = Tv();
        tv.AnswerLoad = load => load.Title == "a.mp4"
            ? new PlaybackStateMessage(PlaybackState.Error, 0, -1, "bad file")
            : LoopbackReceiver.PlaysEverything(load);
        var (_, queue) = await PairedQueueAsync(tv);

        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);

        await Until(() => queue.Status == "The TV could not play a.mp4. Press Next to skip it.");
        tv.Loads.Count.ShouldBe(1);
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2);
    }

    [Fact]
    public async Task WhileTheTvShowsSomethingElse_TheNextFileWaits_AndCanBePlayedOnRequest()
    {
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        var browser = new BrowserPageViewModel(cast);
        _ = new ModeSessionCoordinator(cast, browser);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        SetMirroring(cast, true);

        await EndAsync(tv);

        await Until(() => queue.IsWaiting);
        queue.WaitingText.ShouldBe("b.mp4 is waiting. Living Room is showing your screen.");
        tv.Loads.Count.ShouldBe(1);
        queue.Rows[1].IsWaiting.ShouldBeTrue();
        queue.Rows[1].IsCurrent.ShouldBeFalse("a waiting file is not playing");
        queue.Rows[1].Note.ShouldBe("Waiting for the TV");

        await queue.PlayWaitingCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2);
        queue.IsWaiting.ShouldBeFalse();
        queue.Rows[1].IsWaiting.ShouldBeFalse();
        queue.Rows[1].IsCurrent.ShouldBeTrue();
        cast.IsMirroring.ShouldBeFalse("the person asked for it, so the mirror was stopped");
    }

    [Fact]
    public async Task PlayWaiting_WithNothingWaiting_DoesNothing()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);

        await queue.PlayWaitingCommand.ExecuteAsync(null);

        tv.Loads.ShouldBeEmpty();
        queue.PreviousCommand.CanExecute(null).ShouldBeFalse("connected, but nothing has played to go back to");
        queue.NextCommand.CanExecute(null).ShouldBeFalse();
    }

    /// <summary>A loopback TV that plays whatever it is sent.</summary>
    private static LoopbackReceiver Tv() => new() { AnswerLoad = LoopbackReceiver.PlaysEverything };

    private async Task<(CastPageViewModel Cast, MediaQueueViewModel Queue)> PairedQueueAsync(LoopbackReceiver tv)
    {
        var cast = await PairedAsync(tv, time: clock);
        cast.NowPlaying.UseSettings(settings);
        return (cast, new MediaQueueViewModel(cast, settings, history, new LocalMediaFileSystem(), clock, new Random(1)));
    }

    private static Task EndAsync(LoopbackReceiver tv) =>
        tv.SendAsync(new PlaybackStateMessage(PlaybackState.Ended, 90_000, 90_000));

    private static int Clears(LoopbackReceiver tv) =>
        tv.Received.OfType<MediaCommandMessage>().Count(command => command.Action is MediaAction.Clear);

    private static List<TransportAction> Transports(LoopbackReceiver tv) =>
        [.. tv.Received.OfType<ControlMessage>().Select(control => control.Event).OfType<TransportControl>().Select(transport => transport.Action)];

    private static void SetMirroring(CastPageViewModel cast, bool value) =>
        typeof(CastPageViewModel).GetField("_isMirroring", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cast, value);

    /// <summary>Gives queued work a moment to run, for asserting that nothing happened.</summary>
    private static Task Settle() => Task.Delay(300, TestContext.Current.CancellationToken);

    /// <summary>Real files in a folder of their own, deleted afterwards.</summary>
    private sealed class TempFiles : IDisposable
    {
        public TempFiles()
        {
            Folder = Path.Combine(Path.GetTempPath(), $"flint-queue-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
        }

        public string Folder { get; }

        public string Make(string name, int bytes = 64)
        {
            var path = Path.Combine(Folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
