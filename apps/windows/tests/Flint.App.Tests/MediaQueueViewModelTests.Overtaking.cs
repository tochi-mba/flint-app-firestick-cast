using Flint.App.ViewModels;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>A play overtaken by a newer one stops where it is, and leaves nothing behind to act later.</summary>
public sealed partial class MediaQueueViewModelTests
{
    [Fact]
    public async Task NextPressed_WhileTheResumeQuestionIsOpen_WithdrawsIt_SoItCannotStartTheOldFileLater()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        Remember(a, 600_000);
        var adding = queue.AddDroppedAsync([a, files.Make("b.mp4")]);
        await Until(() => queue.IsAskingResume);

        await queue.NextCommand.ExecuteAsync(null);
        await adding.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        queue.IsAskingResume.ShouldBeFalse();
        clock.Advance(MediaQueueViewModel.ResumeWait * 2);
        await Settle();
        tv.Loads.Select(load => load.Title).ShouldBe(["b.mp4"], "the question's own timer never fires for a.mp4");
        queue.Rows[1].IsCurrent.ShouldBeTrue();
        queue.Rows[0].IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task PlayNow_WhileAFileIsStillSending_EndsThatSend_AndPlaysTheChosenOne()
    {
        await using var tv = new LoopbackReceiver
        {
            // a.mp4 is sent but never confirmed, so it is still in flight when b.mp4 is chosen.
            AnswerLoad = load => load.Title == "a.mp4" ? null : LoopbackReceiver.PlaysEverything(load),
        };
        var (cast, queue) = await PairedQueueAsync(tv);
        var adding = queue.AddDroppedAsync([files.Make("a.mp4"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await queue.PlayNowCommand.ExecuteAsync(queue.Rows[1]);
        await adding.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Clears(tv).ShouldBe(1, "a.mp4 is let go of before b.mp4 is sent");
        tv.Loads.Select(load => load.Title).ShouldBe(["a.mp4", "b.mp4"]);
        cast.NowPlaying.Title.ShouldBe("b.mp4");
        queue.Rows[1].IsCurrent.ShouldBeTrue();
        queue.Rows[0].IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task ChangingAnotherSetting_WhileAPictureShows_LeavesItsCountdownAlone()
    {
        settings.Update(current => current with { Media = current.Media with { PictureSeconds = 10 } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.png"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        clock.Advance(TimeSpan.FromSeconds(6));
        settings.Update(current => current with { Media = current.Media with { VolumeStepPercent = 8 } });
        clock.Advance(TimeSpan.FromSeconds(4));

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("b.mp4");
    }
}
