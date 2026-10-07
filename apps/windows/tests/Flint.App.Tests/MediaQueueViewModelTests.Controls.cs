using Avalonia.Input;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>The Up next list's own controls: arranging it, moving through it, and its keys.</summary>
public sealed partial class MediaQueueViewModelTests
{
    [Fact]
    public async Task PlayNow_SendsThatFile_AndMarksItPlaying()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4"), files.Make("c.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await queue.PlayNowCommand.ExecuteAsync(queue.Rows[2]);

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("c.mp4");
        queue.Rows[2].IsCurrent.ShouldBeTrue();
        queue.Rows[0].IsCurrent.ShouldBeFalse();
        await queue.PlayNowCommand.ExecuteAsync(null);
        tv.Loads.Count.ShouldBe(2);
    }

    [Fact]
    public async Task PlayNext_MovesTheFileStraightAfterThePlayingOne()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4"), files.Make("c.mp4"), files.Make("d.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        queue.PlayNextCommand.Execute(queue.Rows[3]);
        Names(queue).ShouldBe(["a.mp4", "d.mp4", "b.mp4", "c.mp4"]);

        queue.PlayNextCommand.Execute(queue.Rows[0]);
        queue.PlayNextCommand.Execute(null);
        Names(queue).ShouldBe(["a.mp4", "d.mp4", "b.mp4", "c.mp4"], "the playing file and nothing are left where they are");
    }

    [Fact]
    public async Task PlayNext_WithNothingPlaying_MovesTheFileToTheTop()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        using var queue = new MediaQueueViewModel(cast, settings, history, new LocalMediaFileSystem(), clock);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);

        queue.PlayNextCommand.Execute(queue.Rows[1]);

        Names(queue).ShouldBe(["b.mp4", "a.mp4"]);
    }

    [Fact]
    public async Task SeparateDrops_StayInTheOrderTheyWereMade_WhenTheFirstFolderIsSlow()
    {
        using var disk = new SlowFirstDropFileSystem();
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        using var queue = new MediaQueueViewModel(cast, settings, history, disk, clock);

        var first = queue.AddDroppedAsync([SlowFirstDropFileSystem.SlowFolder]);
        disk.WaitUntilFolderWalkStarts();
        var second = queue.AddDroppedAsync([SlowFirstDropFileSystem.FastFile]);
        await Task.Yield();
        second.IsCompleted.ShouldBeFalse("a later drop waits behind the one already being opened");

        disk.FinishFolderWalk();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Names(queue).ShouldBe(["slow.mp4", "fast.mp4"]);
    }

    [Fact]
    public async Task Remove_MoveUp_MoveDown_AndClear_ArrangeTheList()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        using var queue = new MediaQueueViewModel(cast, settings, history, new LocalMediaFileSystem(), clock);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4"), files.Make("c.mp4")]);

        queue.MoveDownCommand.Execute(queue.Rows[0]);
        Names(queue).ShouldBe(["b.mp4", "a.mp4", "c.mp4"]);
        queue.MoveUpCommand.Execute(queue.Rows[2]);
        Names(queue).ShouldBe(["b.mp4", "c.mp4", "a.mp4"]);
        queue.MoveUpCommand.Execute(queue.Rows[0]);
        queue.MoveDownCommand.Execute(queue.Rows[2]);
        queue.MoveUpCommand.Execute(null);
        Names(queue).ShouldBe(["b.mp4", "c.mp4", "a.mp4"], "nothing moves past the ends");

        queue.RemoveCommand.Execute(queue.Rows[1]);
        queue.RemoveCommand.Execute(null);
        Names(queue).ShouldBe(["b.mp4", "a.mp4"]);

        queue.ClearCommand.Execute(null);
        queue.HasItems.ShouldBeFalse();
        queue.Status.ShouldBe("The queue is empty.");
    }

    [Fact]
    public async Task RemovingTheWaitingFile_OrClearing_LetsNothingWait()
    {
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        _ = new ModeSessionCoordinator(cast, new BrowserPageViewModel(cast));
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        SetMirroring(cast, true);
        await EndAsync(tv);
        await Until(() => queue.IsWaiting);

        queue.ClearCommand.Execute(null);
        await queue.PlayWaitingCommand.ExecuteAsync(null);

        tv.Loads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheRowKeys_MoveRemoveAndPlay()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4"), files.Make("c.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        queue.HandleRowKey(queue.Rows[2], Key.Up, KeyModifiers.Alt).ShouldBeTrue();
        Names(queue).ShouldBe(["a.mp4", "c.mp4", "b.mp4"]);
        queue.HandleRowKey(queue.Rows[0], Key.Down, KeyModifiers.Alt).ShouldBeTrue();
        Names(queue).ShouldBe(["c.mp4", "a.mp4", "b.mp4"]);
        queue.HandleRowKey(queue.Rows[2], Key.Delete, KeyModifiers.None).ShouldBeTrue();
        Names(queue).ShouldBe(["c.mp4", "a.mp4"]);
        queue.HandleRowKey(queue.Rows[0], Key.Enter, KeyModifiers.None).ShouldBeTrue();
        await Until(() => tv.Loads.Count == 2);

        queue.HandleRowKey(queue.Rows[0], Key.Up, KeyModifiers.None).ShouldBeFalse("a plain arrow moves focus");
        queue.HandleRowKey(queue.Rows[0], Key.Delete, KeyModifiers.Shift).ShouldBeFalse();
        queue.HandleRowKey(queue.Rows[0], Key.A, KeyModifiers.None).ShouldBeFalse();
        queue.HandleRowKey(null, Key.Delete, KeyModifiers.None).ShouldBeFalse();
    }

    [Fact]
    public void ShuffleAndRepeat_AreSettings_AndTheirButtonsSaySo()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        using var queue = new MediaQueueViewModel(cast, settings, history, new LocalMediaFileSystem(), clock);
        var raised = new List<string?>();
        queue.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        queue.ShuffleLabel.ShouldBe("SHUFFLE OFF");
        queue.ToggleShuffleCommand.Execute(null);
        settings.Current.Media.Shuffle.ShouldBeTrue();
        queue.Shuffle.ShouldBeTrue();
        queue.ShuffleLabel.ShouldBe("SHUFFLE ON");
        raised.ShouldContain(nameof(MediaQueueViewModel.ShuffleLabel));

        queue.RepeatLabel.ShouldBe("REPEAT OFF");
        queue.CycleRepeatCommand.Execute(null);
        queue.Repeat.ShouldBe(RepeatMode.All);
        queue.RepeatLabel.ShouldBe("REPEAT ALL");
        queue.CycleRepeatCommand.Execute(null);
        queue.RepeatLabel.ShouldBe("REPEAT ONE");
        queue.CycleRepeatCommand.Execute(null);
        queue.Repeat.ShouldBe(RepeatMode.Off);

        raised.Clear();
        settings.Update(current => current with { General = current.General with { KeepAwake = false } });
        raised.ShouldBeEmpty("only the Media settings matter to the queue");
    }

    [Fact]
    public async Task Next_FollowsRepeatAll_ButNotRepeatOne()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.One } });

        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("b.mp4");
        queue.NextCommand.CanExecute(null).ShouldBeFalse("repeat one does not make Next wrap");

        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.All } });
        queue.NextCommand.CanExecute(null).ShouldBeTrue();
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 3);
        tv.Loads[2].Title.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task Previous_RestartsAFileThatIsUnderway_AndGoesBackOneThatHasJustStarted()
    {
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2 && cast.NowPlaying.IsActive && !cast.NowPlaying.IsSending);

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 20_000, 90_000));
        await Until(() => cast.NowPlaying.PositionMs >= 20_000);
        await queue.PreviousCommand.ExecuteAsync(null);
        await tv.WaitUntilAsync(sent => sent.OfType<ControlMessage>().Any());
        Transports(tv).ShouldBe([TransportAction.SeekTo]);
        tv.Loads.Count.ShouldBe(2);

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 1_000, 90_000));
        await Until(() => cast.NowPlaying.PositionMs < 3_000);
        await queue.PreviousCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 3);
        tv.Loads[2].Title.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task Previous_AtTheFirstFile_HasNowhereToGo()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        queue.PreviousCommand.CanExecute(null).ShouldBeTrue("a file is playing, so it can be started again");
        await queue.PreviousCommand.ExecuteAsync(null);

        tv.Loads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Next_WhenTheQueueHasEnded_DoesNothing()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await queue.NextCommand.ExecuteAsync(null);

        tv.Loads.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Disposing_StopsListening()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        queue.Dispose();
        await EndAsync(tv);
        settings.Update(current => current with { Media = current.Media with { Shuffle = true } });
        await Settle();

        tv.Loads.Count.ShouldBe(1);
        queue.ShuffleLabel.ShouldBe("SHUFFLE ON", "it reads the settings, but no longer reacts to them");
    }

    [Fact]
    public void EveryPartIsRequired()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        var disk = new LocalMediaFileSystem();

        Should.Throw<ArgumentNullException>(() => new MediaQueueViewModel(null!, settings, history, disk));
        Should.Throw<ArgumentNullException>(() => new MediaQueueViewModel(cast, null!, history, disk));
        Should.Throw<ArgumentNullException>(() => new MediaQueueViewModel(cast, settings, null!, disk));
        Should.Throw<ArgumentNullException>(() => new MediaQueueViewModel(cast, settings, history, null!));
        using var queue = new MediaQueueViewModel(cast, settings, history, disk);
        Should.Throw<ArgumentNullException>(() => queue.AddDroppedAsync(null!));
        Should.Throw<ArgumentNullException>(() => queue.AddAsync(null!));
        Should.Throw<ArgumentNullException>(() => new QueueRowViewModel(null!));
    }

    [Fact]
    public void ARow_SaysWhatKindOfFileItIs()
    {
        var playlist = new Playlist();
        var rows = playlist.Add(["a.mp4", "b.mp3", "c.jpg", "d.flac"])
            .Select(item => new QueueRowViewModel(item))
            .ToArray();
        var unknown = new QueueRowViewModel(new PlaylistItem(99, "e.unknown", MediaFileTypes.For("e.unknown")));

        rows.Select(row => row.KindLabel)
            .Append(unknown.KindLabel)
            .ShouldBe(["VIDEO", "MUSIC", "PICTURE", "MUSIC", "FILE"]);
        rows[0].HasNote.ShouldBeFalse();
        rows[3].HasNote.ShouldBeFalse("every kind Flint offers has played on a TV");
        rows[3].SpokenName.ShouldBe("d.flac");
    }

    [Fact]
    public void ARow_UpdatesItsScreenReaderName_WhenItsStateChanges()
    {
        var item = new Playlist().Add(["a.mp4"]).Single();
        var row = new QueueRowViewModel(item);
        var raised = new List<string?>();
        row.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

        row.IsCurrent = true;
        row.SpokenName.ShouldBe("a.mp4, playing");
        raised.ShouldContain(nameof(QueueRowViewModel.SpokenName));

        raised.Clear();
        row.IsCurrent = false;
        row.IsWaiting = true;
        row.SpokenName.ShouldBe("a.mp4, Waiting for the TV");
        raised.ShouldContain(nameof(QueueRowViewModel.SpokenName));

        raised.Clear();
        row.IsWaiting = false;
        row.IsMissing = true;
        row.SpokenName.ShouldBe("a.mp4, File not found");
        raised.ShouldContain(nameof(QueueRowViewModel.SpokenName));
    }

    [Fact]
    public void AMissingCurrentRow_SaysItIsMissing_NotThatItIsPlaying()
    {
        var row = new QueueRowViewModel(new Playlist().Add(["a.flac"]).Single()) { IsCurrent = true, IsMissing = true };

        row.SpokenName.ShouldBe("a.flac, File not found");
    }

    private static List<string> Names(MediaQueueViewModel queue) => [.. queue.Rows.Select(row => row.Name)];

    private sealed class SlowFirstDropFileSystem : IMediaFileSystem, IDisposable
    {
        public const string SlowFolder = @"D:\slow";
        public const string FastFile = @"D:\fast.mp4";
        private readonly ManualResetEventSlim started = new();
        private readonly ManualResetEventSlim release = new();

        public bool IsFolder(string path) => path == SlowFolder;

        public MediaFileFacts? Facts(string path) => new(1, DateTimeOffset.UnixEpoch);

        public bool IsHiddenOrSystem(string path) => false;

        public IEnumerable<string> FilesIn(string folder)
        {
            started.Set();
            release.Wait(TestContext.Current.CancellationToken);
            return [@"D:\slow\slow.mp4"];
        }

        public IEnumerable<string> FoldersIn(string folder) => [];

        public void WaitUntilFolderWalkStarts() =>
            started.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
                .ShouldBeTrue("the first drop should be walking its folder");

        public void FinishFolderWalk() => release.Set();

        public void Dispose()
        {
            started.Dispose();
            release.Dispose();
        }
    }
}
