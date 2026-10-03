using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core.Media;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>The queue doing no more work than it must: rows reused, positions written only when they change.</summary>
public sealed partial class MediaQueueViewModelTests
{
    [Fact]
    public async Task ArrangingTheList_ReusesItsRows_RatherThanRebuildingThem()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        using var queue = new MediaQueueViewModel(cast, settings, history, new LocalMediaFileSystem(), clock);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4"), files.Make("c.mp4")]);
        var rows = queue.Rows.ToArray();
        var changes = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
        queue.Rows.CollectionChanged += (_, change) => changes.Add(change.Action);

        queue.MoveUpCommand.Execute(rows[2]);
        queue.Rows.ShouldBe([rows[0], rows[2], rows[1]]);
        changes.ShouldBe([System.Collections.Specialized.NotifyCollectionChangedAction.Move]);

        changes.Clear();
        await queue.AddDroppedAsync([files.Make("d.mp4")]);
        changes.ShouldBe([System.Collections.Specialized.NotifyCollectionChangedAction.Add]);
        queue.Rows.Take(3).ShouldBe([rows[0], rows[2], rows[1]]);

        changes.Clear();
        queue.RemoveCommand.Execute(rows[0]);
        changes.ShouldBe([System.Collections.Specialized.NotifyCollectionChangedAction.Remove]);
    }

    [Fact]
    public async Task APausedFile_IsWrittenOnce_NotEveryFewSeconds()
    {
        var counting = new CountingHistory();
        await using var tv = Tv();
        var cast = await PairedAsync(tv, time: clock);
        cast.NowPlaying.UseSettings(settings);
        using var queue = new MediaQueueViewModel(cast, settings, counting, new LocalMediaFileSystem(), clock);
        await queue.AddDroppedAsync([files.Make("a.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Paused, 200_000, 3_600_000));
        await Until(() => counting.Saves == 1);
        clock.Advance(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(5));

        counting.Saves.ShouldBe(1);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(ObjectDisposedException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task WorkNobodyWaitsFor_ThatFailsBecauseTheTvWentAway_DoesNotEndTheApp(Type failure)
    {
        var exception = failure == typeof(ObjectDisposedException)
            ? new ObjectDisposedException("session")
            : (Exception)Activator.CreateInstance(failure, "gone")!;

        await Should.NotThrowAsync(() => MediaQueueViewModel.RunUnobservedAsync(() => Task.FromException(exception)));
    }

    /// <summary>A history that counts how often it is written.</summary>
    private sealed class CountingHistory : IMediaHistoryStore
    {
        private readonly InMemoryMediaHistoryStore inner = new();

        public int Saves { get; private set; }

        public MediaHistoryEntry? Find(string key) => inner.Find(key);

        public void Save(MediaHistoryEntry entry)
        {
            Saves++;
            inner.Save(entry);
        }

        public void Forget(string key) => inner.Forget(key);

        public void Clear() => inner.Clear();
    }
}
