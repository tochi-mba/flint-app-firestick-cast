using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>The queue at its edges: rows that have gone, files of no known length, and repeat-one.</summary>
public sealed partial class MediaQueueViewModelTests
{
    [Fact]
    public async Task Previous_ForAFileOfUnknownLength_GoesBackAnItem_HoweverFarIn()
    {
        await using var tv = new LoopbackReceiver { AnswerLoad = load => new PlaybackStateMessage(PlaybackState.Playing, 0, -1) };
        var (cast, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2 && cast.NowPlaying.IsActive && !cast.NowPlaying.IsSending);
        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 60_000, -1));
        await Until(() => cast.NowPlaying.PositionMs >= 60_000);

        await queue.PreviousCommand.ExecuteAsync(null);

        await Until(() => tv.Loads.Count == 3);
        tv.Loads[2].Title.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task Previous_AfterStop_GoesBackAnItem()
    {
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2 && cast.NowPlaying.IsActive && !cast.NowPlaying.IsSending);
        cast.NowPlaying.StopCommand.Execute(null);
        await Until(() => !cast.NowPlaying.IsActive);

        await queue.PreviousCommand.ExecuteAsync(null);

        await Until(() => tv.Loads.Count == 3);
        tv.Loads[2].Title.ShouldBe("a.mp4");
    }

    [Fact]
    public async Task RepeatOne_WithAMissingNextFile_SkipsToTheOneAfter()
    {
        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.One } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var b = files.Make("b.mp4");
        await queue.AddDroppedAsync([files.Make("a.mp4"), b, files.Make("c.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        File.Delete(b);

        await queue.NextCommand.ExecuteAsync(null);

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("c.mp4");
    }

    [Fact]
    public async Task RepeatOne_OnATimedPicture_MovesOnRatherThanShowingItForever()
    {
        settings.Update(current => current with { Media = current.Media with { Repeat = RepeatMode.One, PictureSeconds = 6 } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.png"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        clock.Advance(TimeSpan.FromSeconds(6));

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("b.mp4");
    }

    [Fact]
    public async Task ChangingAPictureTime_ThatWasAlreadyRunning_StartsItAgain()
    {
        settings.Update(current => current with { Media = current.Media with { PictureSeconds = 60 } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.png"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        clock.Advance(TimeSpan.FromSeconds(30));

        settings.Update(current => current with { Media = current.Media with { PictureSeconds = 10 } });
        clock.Advance(TimeSpan.FromSeconds(10));

        await Until(() => tv.Loads.Count == 2);
    }

    [Fact]
    public async Task RowsThatHaveGone_AreLeftAlone()
    {
        var cast = Snapshots.SnapshotFixtures.ViewModel();
        using var queue = new MediaQueueViewModel(cast, settings, history, new LocalMediaFileSystem(), clock);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        var gone = queue.Rows[0];
        queue.RemoveCommand.Execute(gone);

        await queue.PlayNowCommand.ExecuteAsync(gone);
        queue.MoveUpCommand.Execute(gone);
        queue.MoveDownCommand.Execute(gone);

        queue.Rows.Select(row => row.Name).ShouldBe(["b.mp4"]);
    }

    [Fact]
    public async Task PlayNext_OnAFileAboveThePlayingOne_MovesItDownToJustAfter()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4"), files.Make("c.mp4"), files.Make("d.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        await queue.PlayNowCommand.ExecuteAsync(queue.Rows[2]);
        await Until(() => tv.Loads.Count == 2);

        queue.PlayNextCommand.Execute(queue.Rows[0]);

        queue.Rows.Select(row => row.Name).ShouldBe(["b.mp4", "c.mp4", "a.mp4", "d.mp4"]);
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 3);
        tv.Loads[2].Title.ShouldBe("a.mp4");
    }
}
