using Flint.App.ViewModels;
using Flint.Core.Media;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>
/// The Now Playing card over a real session: a file sent, the TV's reports, and controls reaching it.
/// </summary>
public sealed class CastPageNowPlayingTests : IDisposable
{
    private readonly ManualTime clock = new();
    private readonly string file = Path.Combine(Path.GetTempPath(), $"flint-now-{Guid.NewGuid():N}.mp4");

    public CastPageNowPlayingTests() => File.WriteAllBytes(file, new byte[64 * 1024]);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string FileName => Path.GetFileName(file);

    [Fact]
    public async Task SendingAFile_ShowsTheCard_ThenWhatTheTvSays()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);

        var sending = cast.LoadMediaFileCommand.ExecuteAsync(file);
        await receiver.WaitForAsync<MediaCommandMessage>();
        cast.NowPlaying.IsActive.ShouldBeTrue();
        cast.NowPlaying.Title.ShouldBe(FileName);
        cast.NowPlaying.IsSending.ShouldBeTrue();

        await receiver.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 0, 90_000));
        await sending.WaitAsync(TimeSpan.FromSeconds(10), Token);

        cast.IsMediaPlaying.ShouldBeTrue();
        cast.NowPlaying.IsSending.ShouldBeFalse();
        cast.NowPlaying.Phase.ShouldBe(PlaybackPhase.Playing);
        cast.NowPlaying.DurationMs.ShouldBe(90_000);
    }

    [Fact]
    public async Task ChoosingAFile_WithNoTvConnected_SaysToConnectFirst_AndSendsNothing()
    {
        var cast = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice())).Cast;

        await cast.LoadMediaFileCommand.ExecuteAsync(@"C:ideos\Holiday in Lisbon.mp4");

        cast.Failure.ShouldBe("The receiver session ended. Connect it again before choosing media.");
        cast.NowPlaying.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task ThePagesOwnReports_ReachTheCard()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        await receiver.SendAsync(new PlaybackStateMessage(PlaybackState.Paused, 30_000, 90_000));
        await Until(() => cast.NowPlaying.Phase == PlaybackPhase.Paused);

        cast.NowPlaying.PositionMs.ShouldBe(30_000);
    }

    [Fact]
    public async Task ATvClearedWithItsOwnRemote_HidesTheCard_AndSaysSo()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        await receiver.SendAsync(new PlaybackStateMessage(PlaybackState.Idle));
        await Until(() => !cast.NowPlaying.IsActive);

        cast.IsMediaPlaying.ShouldBeFalse();
        cast.MediaStatus.ShouldBe("Playback ended on the TV.");
    }

    [Fact]
    public async Task AnIdleReport_WithNothingShowing_ChangesNothing()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);
        var before = cast.MediaStatus;

        cast.OnPlaybackReported(CurrentSession(cast), new PlaybackStateMessage(PlaybackState.Idle));

        cast.MediaStatus.ShouldBe(before);
    }

    [Fact]
    public async Task Controls_ReachTheTv()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        cast.NowPlaying.PlayPauseCommand.Execute(null);
        cast.NowPlaying.VolumePercent = 40;

        await receiver.WaitUntilAsync(sent => sent.OfType<ControlMessage>().Count() == 2);
        receiver.Received.OfType<ControlMessage>().Select(control => control.Event).ShouldBe(
        [
            new TransportControl(TransportAction.Pause),
            new VolumeControl(0.4f),
        ]);
    }

    [Fact]
    public async Task Stop_ClearsTheTv_AndHidesTheCard()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        cast.NowPlaying.StopCommand.Execute(null);
        await receiver.WaitUntilAsync(sent => sent.OfType<MediaCommandMessage>().Any(command => command.Action == MediaAction.Clear));
        await Until(() => !cast.NowPlaying.IsActive);

        cast.IsMediaPlaying.ShouldBeFalse();
        cast.MediaStatus.ShouldBe("Playback stopped.");
    }

    [Fact]
    public async Task AFileTheTvCouldNotPlay_ShowsItsReason_AndTryAgainSendsItAgain()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);

        var sending = cast.LoadMediaFileCommand.ExecuteAsync(file);
        await receiver.WaitForAsync<MediaCommandMessage>();
        await receiver.SendAsync(new PlaybackStateMessage(PlaybackState.Error, Detail: "unsupported codec"));
        await sending.WaitAsync(TimeSpan.FromSeconds(10), Token);

        cast.NowPlaying.ShowsProblem.ShouldBeTrue();
        cast.NowPlaying.ProblemText.ShouldBe("unsupported codec");

        cast.NowPlaying.TryAgainCommand.Execute(null);
        await receiver.WaitUntilAsync(sent => sent.OfType<MediaCommandMessage>().Count(command => command.Action == MediaAction.Load) == 2);
        cast.NowPlaying.IsSending.ShouldBeTrue("the same file is on its way again");
    }

    [Fact]
    public async Task StoppingAFileThatNeverStarted_StillClearsTheTv()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);
        var sending = cast.LoadMediaFileCommand.ExecuteAsync(file);
        await receiver.WaitForAsync<MediaCommandMessage>();
        await receiver.SendAsync(new PlaybackStateMessage(PlaybackState.Error, Detail: "unsupported codec"));
        await sending.WaitAsync(TimeSpan.FromSeconds(10), Token);

        cast.NowPlaying.StopCommand.Execute(null);

        await receiver.WaitUntilAsync(sent => sent.OfType<MediaCommandMessage>().Any(command => command.Action == MediaAction.Clear));
        await Until(() => !cast.NowPlaying.IsActive);
    }

    [Fact]
    public async Task CancellingASend_TellsTheTvToDropIt_AndKeepsTheConnection()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);
        var large = Path.Combine(Path.GetTempPath(), $"flint-large-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(large, new byte[24 * 1024 * 1024], Token);
        try
        {
            var sending = cast.LoadMediaFileCommand.ExecuteAsync(large);
            await receiver.WaitForAsync<MediaDataMessage>();

            cast.NowPlaying.CancelCommand.Execute(null);
            await sending.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await receiver.WaitUntilAsync(sent => sent.OfType<MediaCommandMessage>().Any(command => command.Action == MediaAction.Clear));

            cast.NowPlaying.IsActive.ShouldBeFalse();
            cast.MediaStatus.ShouldBe("Sending cancelled.");
            cast.Failure.ShouldBeNull();
            cast.IsSessionConnected.ShouldBeTrue();
            receiver.Received.OfType<MediaDataMessage>().ShouldNotContain(chunk => chunk.IsFinal);
        }
        finally
        {
            File.Delete(large);
        }
    }

    [Fact]
    public async Task CancellingWhenNothingIsSending_DoesNothing()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        await ((IMediaRemote)cast).CancelSendAsync();

        cast.NowPlaying.Phase.ShouldBe(PlaybackPhase.Playing);
    }

    [Fact]
    public async Task ATvThatNeverConfirms_ShowsTheFirewallGuidanceOnTheCard()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);

        var sending = cast.LoadMediaFileCommand.ExecuteAsync(file);
        await receiver.WaitForAsync<MediaCommandMessage>();
        clock.Advance(TimeSpan.FromSeconds(61));
        await sending.WaitAsync(TimeSpan.FromSeconds(10), Token);

        cast.MediaStatus.ShouldBe("The TV did not confirm playback.");
        cast.NowPlaying.ShowsProblem.ShouldBeTrue();
        cast.NowPlaying.ProblemText.ShouldBe(CastPageViewModel.FirewallGuidance);
    }

    [Fact]
    public async Task TheConnectionDropping_GreysTheCard_AndSaysWhy()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        await receiver.CloseAsync();
        await Until(() => cast.NowPlaying.IsConnectionLost);

        cast.NowPlaying.IsActive.ShouldBeTrue();
        cast.NowPlaying.Title.ShouldBe(FileName);
        cast.NowPlaying.ConnectionLostText.ShouldBe("The connection to Living Room was lost.");
    }

    [Fact]
    public async Task Disconnecting_GreysTheCard_AndSaysSo()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        await cast.DisconnectCommand.ExecuteAsync(null);

        cast.NowPlaying.IsConnectionLost.ShouldBeTrue();
        cast.NowPlaying.ConnectionLostText.ShouldBe("Disconnected from Living Room.");
    }

    [Fact]
    public async Task AReportFromASessionNoLongerInUse_IsIgnored()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);
        await using var other = new LoopbackReceiver();
        await using var stale = await Flint.Session.CastSession.ConnectAsync(
            System.Net.IPAddress.Loopback, other.Port, "123456", cancellationToken: Token);

        cast.OnPlaybackReported(stale, new PlaybackStateMessage(PlaybackState.Paused, 5_000, 90_000));

        cast.NowPlaying.Phase.ShouldBe(PlaybackPhase.Playing);
    }

    [Fact]
    public async Task ControlsWithNoLiveSession_FailAsAnySendWould()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);
        await cast.DisconnectCommand.ExecuteAsync(null);
        var remote = (IMediaRemote)cast;

        await Should.ThrowAsync<IOException>(() => remote.SendTransportAsync(TransportAction.Play));
        await Should.ThrowAsync<IOException>(() => remote.SetVolumeAsync(0.5f));
        await Should.ThrowAsync<IOException>(remote.StopAsync);
    }

    [Fact]
    public async Task DroppingAPartialFile_OnAConnectionAlreadyGone_IsQuiet()
    {
        await using var receiver = new LoopbackReceiver();
        var session = await Flint.Session.CastSession.ConnectAsync(
            System.Net.IPAddress.Loopback, receiver.Port, "123456", cancellationToken: Token);
        await session.DisposeAsync();

        await Should.NotThrowAsync(() => CastPageViewModel.DropPartialFileAsync(session));
    }

    [Fact]
    public async Task ControlsOnASessionThatHasJustEnded_FailAsAnySendWould()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);
        var ended = CurrentSession(cast);
        await ended.DisposeAsync();

        // Still the page's session for a moment, until the page hears it ended.
        await Should.ThrowAsync<IOException>(() => ((IMediaRemote)cast).SendTransportAsync(TransportAction.Play));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task OnTheUiThread_ReportsArriveThere()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PlayingAsync(receiver);

        await receiver.SendAsync(new PlaybackStateMessage(PlaybackState.Paused, 45_000, 90_000));
        await Until(() => cast.NowPlaying.Phase == PlaybackPhase.Paused);

        Avalonia.Threading.Dispatcher.UIThread.CheckAccess().ShouldBeTrue();
        cast.NowPlaying.PositionMs.ShouldBe(45_000);
    }

    [Fact]
    public async Task TryAgain_BeforeAnyFile_DoesNothing()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, time: clock);

        await ((IMediaRemote)cast).TryAgainAsync();

        cast.NowPlaying.IsActive.ShouldBeFalse();
        receiver.Received.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PlaybackState.Idle, PlaybackPhase.Idle)]
    [InlineData(PlaybackState.Buffering, PlaybackPhase.Buffering)]
    [InlineData(PlaybackState.Playing, PlaybackPhase.Playing)]
    [InlineData(PlaybackState.Paused, PlaybackPhase.Paused)]
    [InlineData(PlaybackState.Ended, PlaybackPhase.Finished)]
    [InlineData(PlaybackState.Error, PlaybackPhase.Problem)]
    public void EachReportedState_IsReadAsItsPhase(PlaybackState state, PlaybackPhase phase)
    {
        var cast = SnapshotlessCast();

        cast.ToSnapshot(new PlaybackStateMessage(state, 1_000, 2_000, "Paused")).Phase.ShouldBe(phase);
    }

    [Fact]
    public void AnErrorReport_IsWordedAsTheMediaPageWordsIt()
    {
        var cast = SnapshotlessCast();

        var snapshot = cast.ToSnapshot(new PlaybackStateMessage(PlaybackState.Error, Detail: "error code io network connection timeout"));

        snapshot.Detail.ShouldBe(CastPageViewModel.FirewallGuidance);
        snapshot.ReceivedAt.ShouldBe(clock.GetUtcNow());
    }

    public void Dispose() => File.Delete(file);

    private async Task<CastPageViewModel> PlayingAsync(LoopbackReceiver receiver)
    {
        var cast = await PairedAsync(receiver, time: clock);
        var sending = cast.LoadMediaFileCommand.ExecuteAsync(file);
        await receiver.WaitForAsync<MediaCommandMessage>();
        await receiver.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 0, 90_000));
        await sending.WaitAsync(TimeSpan.FromSeconds(10), Token);
        cast.NowPlaying.Phase.ShouldBe(PlaybackPhase.Playing);
        return cast;
    }

    private CastPageViewModel SnapshotlessCast() => new(
        BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
        new NoRecentAddresses(),
        receiverInstaller: new Flint.App.Services.OfflineReceiverInstaller(),
        time: clock);

    private static Flint.Session.CastSession CurrentSession(CastPageViewModel cast) =>
        (Flint.Session.CastSession)typeof(CastPageViewModel)
            .GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(cast)!;
}
