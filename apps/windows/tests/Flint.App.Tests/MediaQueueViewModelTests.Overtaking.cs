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
    public async Task AnOldResumeTimerCallback_CannotAnswerANewerFilesQuestion()
    {
        var lateTime = new LateCallbackTime();
        await using var tv = Tv();
        var cast = await PairedAsync(tv, time: clock);
        cast.NowPlaying.UseSettings(settings);
        using var queue = new MediaQueueViewModel(
            cast,
            settings,
            history,
            new Flint.App.Services.LocalMediaFileSystem(),
            lateTime,
            new Random(1));
        var a = files.Make("a.mp4");
        var b = files.Make("b.mp4");
        Remember(a, 300_000);
        Remember(b, 600_000);

        var adding = queue.AddDroppedAsync([a, b]);
        await Until(() => queue.ResumeText?.Contains("a.mp4", StringComparison.Ordinal) == true);
        var oldTimer = lateTime.Created.Single();

        var choosing = queue.PlayNowCommand.ExecuteAsync(queue.Rows[1]);
        await Until(() => queue.ResumeText?.Contains("b.mp4", StringComparison.Ordinal) == true);
        oldTimer.FireEvenThoughDisposed();
        await Settle();

        queue.IsAskingResume.ShouldBeTrue("the late callback belongs to a.mp4, not b.mp4");
        tv.Loads.ShouldBeEmpty();
        queue.StartOverCommand.Execute(null);
        await choosing;
        await adding.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].Title.ShouldBe("b.mp4");
        tv.Loads[0].StartPositionMs.ShouldBe(0);
    }

    [Fact]
    public async Task PlayNow_WhileAFileIsStillSending_EndsThatSend_BeforeAResumeQuestion()
    {
        await using var tv = new LoopbackReceiver
        {
            // a.mp4 is sent but never confirmed, so it is still in flight when b.mp4 is chosen.
            AnswerLoad = load => load.Title == "a.mp4" ? null : LoopbackReceiver.PlaysEverything(load),
        };
        var (cast, queue) = await PairedQueueAsync(tv);
        var b = files.Make("b.mp4");
        Remember(b, 600_000);
        var adding = queue.AddDroppedAsync([files.Make("a.mp4"), b]);
        await Until(() => tv.Loads.Count == 1);

        var choosing = queue.PlayNowCommand.ExecuteAsync(queue.Rows[1]);
        await Until(() => queue.IsAskingResume);
        await Until(() => Clears(tv) == 1);

        tv.Loads.Select(load => load.Title).ShouldBe(["a.mp4"], "b.mp4 waits for the answer");
        queue.StartOverCommand.Execute(null);
        await choosing;
        await adding.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Clears(tv).ShouldBe(1, "a.mp4 is let go of before b.mp4 is sent");
        tv.Loads.Select(load => load.Title).ShouldBe(["a.mp4", "b.mp4"]);
        cast.NowPlaying.Title.ShouldBe("b.mp4");
        queue.Rows[1].IsCurrent.ShouldBeTrue();
        queue.Rows[0].IsCurrent.ShouldBeFalse();
    }

    [Fact]
    public async Task ADrop_WhileTheOneBeforeAsksWhetherToResume_JoinsTheQueueAtOnce()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        Remember(a, 600_000);
        var asking = queue.AddDroppedAsync([a]);
        await Until(() => queue.IsAskingResume);

        await queue.AddDroppedAsync([files.Make("b.mp4")]).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Names(queue).ShouldBe(["a.mp4", "b.mp4"]);
        queue.IsAskingResume.ShouldBeTrue("the question is still the person's to answer");
        queue.StartOverCommand.Execute(null);
        await asking;
        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].Title.ShouldBe("a.mp4");
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

    private sealed class LateCallbackTime : TimeProvider
    {
        public List<LateTimer> Created { get; } = [];

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new LateTimer(callback, state);
            Created.Add(timer);
            return timer;
        }

        public sealed class LateTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void FireEvenThoughDisposed() => callback(state);

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
