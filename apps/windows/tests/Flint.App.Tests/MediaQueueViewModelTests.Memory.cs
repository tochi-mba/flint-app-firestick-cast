using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>What the queue remembers about where files stopped, and how pictures take their turn.</summary>
public sealed partial class MediaQueueViewModelTests
{
    [Fact]
    public async Task AFilePlayedBefore_AsksWhetherToResume_AndResumingStartsWhereItStopped()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        Remember(a, 600_000);

        var adding = queue.AddDroppedAsync([a]);
        await Until(() => queue.IsAskingResume);
        queue.ResumeText.ShouldBe("Resume a.mp4 from 10:00?");
        tv.Loads.ShouldBeEmpty("nothing is sent until the question is answered");

        queue.ResumeFromWhereItStoppedCommand.Execute(null);
        await adding;

        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].StartPositionMs.ShouldBe(600_000);
        queue.IsAskingResume.ShouldBeFalse();
    }

    [Fact]
    public async Task StartingOver_PlaysFromTheStart()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        Remember(a, 600_000);

        var adding = queue.AddDroppedAsync([a]);
        await Until(() => queue.IsAskingResume);
        queue.StartOverCommand.Execute(null);
        await adding;

        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].StartPositionMs.ShouldBe(0);
    }

    [Fact]
    public async Task NobodyAnswering_ResumesAfterEightSeconds()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        Remember(a, 600_000);

        var adding = queue.AddDroppedAsync([a]);
        await Until(() => queue.IsAskingResume);
        clock.Advance(TimeSpan.FromSeconds(7));
        queue.IsAskingResume.ShouldBeTrue();
        clock.Advance(TimeSpan.FromSeconds(1));
        await adding;

        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].StartPositionMs.ShouldBe(600_000);
    }

    [Theory]
    [InlineData(ResumeMode.Resume, 600_000)]
    [InlineData(ResumeMode.StartOver, 0)]
    public async Task WithResumeOrStartOverChosen_NothingIsAsked(ResumeMode mode, long expected)
    {
        settings.Update(current => current with { Media = current.Media with { PlayedBefore = mode } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        Remember(a, 600_000);

        await queue.AddDroppedAsync([a]);

        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].StartPositionMs.ShouldBe(expected);
        queue.IsAskingResume.ShouldBeFalse();
    }

    [Fact]
    public async Task WithRememberingOff_EveryFileStartsAtTheStart_AndNothingIsKept()
    {
        settings.Update(current => current with { Media = current.Media with { RememberPlayback = false } });
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        Remember(a, 600_000);
        history.Entries.Count.ShouldBe(1);

        await queue.AddDroppedAsync([a]);
        await Until(() => tv.Loads.Count == 1);
        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 120_000, 3_600_000));
        await Until(() => cast.NowPlaying.PositionMs >= 120_000);
        clock.Advance(TimeSpan.FromSeconds(5));

        tv.Loads[0].StartPositionMs.ShouldBe(0);
        history.Entries.Single().PositionMs.ShouldBe(600_000, "what was kept before stays until it is cleared");
    }

    [Fact]
    public async Task APositionWorthKeeping_IsKeptEveryFiveSeconds_AndForgottenWhenTheFileEnds()
    {
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        await queue.AddDroppedAsync([a, files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 10_000, 3_600_000));
        await Until(() => cast.NowPlaying.PositionMs >= 10_000 && cast.NowPlaying.DurationMs == 3_600_000);
        clock.Advance(TimeSpan.FromSeconds(5));
        history.Entries.ShouldBeEmpty("ten seconds in is not worth offering back");

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 125_000, 3_600_000));
        await Until(() => cast.NowPlaying.PositionMs >= 125_000);
        clock.Advance(TimeSpan.FromSeconds(5));
        history.Entries.Single().Key.ShouldBe(KeyOf(a));
        history.Entries.Single().DurationMs.ShouldBe(3_600_000);

        await EndAsync(tv);
        await Until(() => tv.Loads.Count == 2);
        history.Entries.ShouldNotContain(entry => entry.Key == KeyOf(a));
    }

    [Fact]
    public async Task PausingAndStopping_KeepThePosition()
    {
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        await queue.AddDroppedAsync([a]);
        await Until(() => tv.Loads.Count == 1);

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Paused, 200_000, 3_600_000));
        await Until(() => history.Entries.Count == 1);
        history.Entries.Single().PositionMs.ShouldBe(200_000);

        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 300_000, 3_600_000));
        await Until(() => cast.NowPlaying.PositionMs >= 300_000);
        cast.NowPlaying.StopCommand.Execute(null);
        await Until(() => !cast.NowPlaying.IsActive);
        history.Entries.Single().PositionMs.ShouldBeGreaterThanOrEqualTo(300_000);
    }

    [Fact]
    public async Task LosingTheConnection_KeepsThePosition_AndStopsWaitingAndAsking()
    {
        await using var tv = Tv();
        var (cast, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        await queue.AddDroppedAsync([a, files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 400_000, 3_600_000));
        await Until(() => cast.NowPlaying.PositionMs >= 400_000);

        await tv.CloseAsync();

        await Until(() => cast.NowPlaying.IsConnectionLost);
        history.Entries.Single().PositionMs.ShouldBeGreaterThanOrEqualTo(400_000);
        queue.IsWaiting.ShouldBeFalse();
        queue.IsAskingResume.ShouldBeFalse();
        queue.NextCommand.CanExecute(null).ShouldBeFalse();
        clock.ActiveTimers.ShouldBe(0, "the card's and the queue's timers have all stopped");
    }

    [Fact]
    public async Task APicture_StaysUntilThePersonMovesOn_WhateverTheTvSays()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.jpg"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);
        tv.Loads[0].MimeType.ShouldBe("image/jpeg");

        await EndAsync(tv);
        clock.Advance(TimeSpan.FromMinutes(5));
        await Settle();

        tv.Loads.Count.ShouldBe(1);
        await queue.NextCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2);
    }

    [Fact]
    public async Task WithAPictureTime_ThePictureMovesOnByItself()
    {
        settings.Update(current => current with { Media = current.Media with { PictureSeconds = 10 } });
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.png"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        clock.Advance(TimeSpan.FromSeconds(9));
        await Settle();
        tv.Loads.Count.ShouldBe(1);
        clock.Advance(TimeSpan.FromSeconds(1));

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].Title.ShouldBe("b.mp4");
    }

    [Fact]
    public async Task ChangingThePictureTime_WhileAPictureShows_StartsTheNewTime()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        await queue.AddDroppedAsync([files.Make("a.png"), files.Make("b.mp4")]);
        await Until(() => tv.Loads.Count == 1);

        settings.Update(current => current with { Media = current.Media with { PictureSeconds = 6 } });
        clock.Advance(TimeSpan.FromSeconds(6));

        await Until(() => tv.Loads.Count == 2);
    }

    [Fact]
    public async Task APictureIsNeverOfferedBackToResume()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.jpg");
        Remember(a, 600_000);

        await queue.AddDroppedAsync([a]);

        await Until(() => tv.Loads.Count == 1);
        queue.IsAskingResume.ShouldBeFalse();
        tv.Loads[0].StartPositionMs.ShouldBe(0);
    }

    [Fact]
    public async Task ThePlayingFileIsGoneWhenItEnds_SoItIsNotRemembered()
    {
        await using var tv = Tv();
        var (_, queue) = await PairedQueueAsync(tv);
        var a = files.Make("a.mp4");
        await queue.AddDroppedAsync([a]);
        await Until(() => tv.Loads.Count == 1);
        queue.ClearCommand.Execute(null);

        await EndAsync(tv);
        await Settle();

        history.Entries.ShouldBeEmpty();
        queue.Status.ShouldBe("The queue is empty.");
    }

    private void Remember(string path, long positionMs) =>
        history.Save(new MediaHistoryEntry(KeyOf(path), positionMs, 3_600_000, clock.GetUtcNow()));

    private static string KeyOf(string path)
    {
        var file = new FileInfo(path);
        return ResumePolicy.KeyFor(path, file.Length, file.LastWriteTimeUtc);
    }
}
