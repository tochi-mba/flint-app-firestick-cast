using System.Globalization;
using Flint.Core.Media;
using Flint.Protocol;
using Shouldly;
using Xunit;

namespace Flint.Hardware.E2E;

/// <summary>
/// What the real TV does with the files and controls the Media page sends. Opt-in, like every
/// hardware flow: see <see cref="HardwareGate"/>. Each test writes a report and screenshots under
/// artifacts/hardware.
/// </summary>
public sealed class MediaHardwareTests
{
    private static readonly TimeSpan Short = TimeSpan.FromSeconds(6);

    /// <summary>What the Media page asks of a picture: stay up a day, so the queue decides when it goes.</summary>
    private const long PictureHoldMs = 24 * 60 * 60 * 1000;

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task EveryFileTypeFlintOffers_PlaysOnTheTv()
    {
        await using var tv = await PairedTv.OpenAsync("file-types");
        var failed = new List<string>();
        foreach (var extension in MediaFileTypes.Extensions.Order(StringComparer.Ordinal))
        {
            var type = MediaFileTypes.For("file" + extension);
            var path = await TestMedia.PathForAsync(extension, tv.Token);
            var started = await tv.PlayAsync(path);
            await Task.Delay(TimeSpan.FromSeconds(2), tv.Token);
            await tv.ScreenshotAsync("type" + extension.Replace('.', '-'));
            var verdict = started is null ? "no answer" : $"{started.State} {started.Detail}".Trim();
            tv.Note($"{extension,-6} {type.MimeType,-18} {verdict}");
            if (started?.State is not PlaybackState.Playing)
            {
                failed.Add(extension);
            }

            await tv.ClearAsync();
        }

        failed.ShouldBeEmpty("every file type Flint offers must play on the TV");
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task PauseSeekAndPlay_ReachThePlayer_AndItsReportsFollow()
    {
        await using var tv = await PairedTv.OpenAsync("controls");
        var started = await tv.PlayAsync(await TestMedia.PathForAsync(".mp4", tv.Token));
        started?.State.ShouldBe(PlaybackState.Playing);
        tv.Note($"Started: {started?.State} at {started?.PositionMs} ms of {started?.DurationMs} ms.");
        await Task.Delay(TimeSpan.FromSeconds(3), tv.Token);

        var before = tv.Now;
        await tv.Session.SendTransportAsync(TransportAction.Pause, cancellationToken: tv.Token);
        var paused = await tv.WaitForAsync(report => report.State is PlaybackState.Paused, Short, before);
        tv.Note($"Pause: {Describe(paused)}.");
        paused.ShouldNotBeNull("the TV reports a pause");

        before = tv.Now;
        await tv.Session.SendTransportAsync(TransportAction.SeekTo, 30_000, tv.Token);
        var sought = await tv.WaitForAsync(report => Math.Abs(report.PositionMs - 30_000) < 1_500, Short, before);
        tv.Note($"Seek to 30 s while paused: {Describe(sought)}.");
        sought.ShouldNotBeNull("the TV reports the new position");
        await tv.ScreenshotAsync("paused-at-30s");

        before = tv.Now;
        await tv.Session.SendTransportAsync(TransportAction.Play, cancellationToken: tv.Token);
        var playing = await tv.WaitForAsync(report => report.State is PlaybackState.Playing && report.PositionMs >= 29_000, Short, before);
        tv.Note($"Play: {Describe(playing)}.");
        playing.ShouldNotBeNull();

        await Task.Delay(TimeSpan.FromSeconds(4), tv.Token);
        var later = tv.Reports[^1].Report;
        var moved = later.PositionMs - playing!.PositionMs;
        tv.Note($"Four seconds later: {Describe(later)}, {moved} ms further on.");
        moved.ShouldBeInRange(2_500, 5_500, "the position moves at the speed of the clock");

        before = tv.Now;
        await tv.Session.SendTransportAsync(TransportAction.Stop, cancellationToken: tv.Token);
        var stopped = await tv.WaitForAsync(report => report.State is PlaybackState.Idle, Short, before);
        tv.Note($"Stop: {Describe(stopped)}.");
        await tv.ScreenshotAsync("after-stop");
        stopped.ShouldNotBeNull("stopping returns the TV to idle");
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task AFileResumedPartWay_StartsWithinASecondOfWhereItStopped()
    {
        await using var tv = await PairedTv.OpenAsync("resume");
        var before = tv.Now;
        var started = await tv.PlayAsync(await TestMedia.PathForAsync(".mp4", tv.Token), startPositionMs: 40_000);
        var firstPlaying = await tv.WaitForAsync(report => report.State is PlaybackState.Playing, Short, before);
        tv.Note($"Asked to start at 40 s. Start: {Describe(started)}. First playing report: {Describe(firstPlaying)}.");
        await tv.ScreenshotAsync("resumed");

        firstPlaying.ShouldNotBeNull();
        firstPlaying.PositionMs.ShouldBeInRange(39_000, 41_500);
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task APicture_AskedToStayUp_OutlastsTheTvsSixSeconds_AndItsBarGoes()
    {
        await using var tv = await PairedTv.OpenAsync("picture");
        var before = tv.Now;
        var started = await tv.PlayAsync(await TestMedia.PathForAsync(".png", tv.Token), durationMs: PictureHoldMs);
        tv.Note($"Start, asked to stay up a day: {Describe(started)}.");
        await Task.Delay(TimeSpan.FromSeconds(2), tv.Token);
        await tv.ScreenshotAsync("picture-at-2s");
        await Task.Delay(TimeSpan.FromSeconds(10), tv.Token);
        await tv.ScreenshotAsync("picture-at-12s");

        var states = tv.Reports.Where(entry => entry.At >= before).Select(entry => entry.Report.State).Distinct().ToList();
        tv.Note($"States over twelve seconds: {string.Join(", ", states)}.");
        started?.State.ShouldBe(PlaybackState.Playing);
        states.ShouldNotContain(PlaybackState.Ended, "the TV keeps a picture up as long as it is asked to");
    }

    [Fact]
    [Trait("Category", "Hardware")]
    public async Task TheGapBetweenTwoQueuedFiles_IsTheTimeToSendTheSecond()
    {
        await using var tv = await PairedTv.OpenAsync("queue-gap");
        var before = tv.Now;
        (await tv.PlayAsync(await TestMedia.PathForAsync(".mp3", tv.Token)))?.State.ShouldBe(PlaybackState.Playing);
        var ended = await tv.WaitForAsync(report => report.State is PlaybackState.Ended, TimeSpan.FromSeconds(30), before);
        ended.ShouldNotBeNull("twenty seconds of sound ends");
        var endedAt = tv.Now;

        var second = await TestMedia.PathForAsync(".mp4", tv.Token);
        var next = await tv.PlayAsync(second);
        var gap = tv.Now - endedAt;
        tv.Note(string.Create(
            CultureInfo.InvariantCulture,
            $"Second file ({new FileInfo(second).Length / 1024} KiB) playing {gap.TotalSeconds:F1} s after the first ended: {Describe(next)}."));
        next?.State.ShouldBe(PlaybackState.Playing);
        gap.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    private static string Describe(PlaybackStateMessage? report) =>
        report is null ? "nothing reported in time" : $"{report.State} at {report.PositionMs} ms of {report.DurationMs} ms";
}
