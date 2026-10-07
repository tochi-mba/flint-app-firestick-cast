using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.Services;
using Flint.App.Tests.Snapshots;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Flint.Protocol;
using Flint.Session;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>
/// The Cast page when the TV goes away, and when the person ends the connection themselves.
/// </summary>
public sealed class CastPageDisconnectTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheTvClosingWhileIdle_PutsThePageBackToTvFound_AndSaysWhy()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);

        await receiver.SendByeAsync();
        await Ended(cast);

        cast.SessionStatus.ShouldBe("TV found");
        cast.PairingStatus.ShouldBe("Living Room closed the connection.");
        cast.ShowPairing.ShouldBeTrue("the code box is back, ready to pair again");
        cast.IsSessionConnected.ShouldBeFalse();
        cast.CanStartMirrorNow.ShouldBeFalse();
        cast.CanDisconnect.ShouldBeFalse();
        cast.DisconnectCommand.CanExecute(null).ShouldBeFalse();
        cast.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task TheTvHangingUp_ReadsAsALostConnection()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);

        await receiver.CloseAsync();
        await Ended(cast);

        cast.PairingStatus.ShouldBe("The connection to Living Room was lost.");
    }

    [Fact]
    public async Task TheTvClosingWhileSharing_EndsTheMirrorQuietly_AndTheScreenPageSaysWhy()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, new IdleMirrorEngine());
        var sharing = cast.StartScreenSessionCommand.ExecuteAsync(null);
        await receiver.WaitForAsync<VideoConfigMessage>();
        cast.IsMirroring.ShouldBeTrue();

        await receiver.SendByeAsync();
        await sharing.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await Ended(cast);

        cast.IsMirroring.ShouldBeFalse();
        cast.MirrorStatus.ShouldBe("Living Room closed the connection.");
        cast.Failure.ShouldBeNull("the end is told as what happened, not as an error");
    }

    [Fact]
    public async Task TheTvGoingDuringAFileSend_StopsTheSend_AndLeavesNothingPlaying()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);
        var file = Path.Combine(Path.GetTempPath(), $"flint-send-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(file, new byte[3 * 1024 * 1024], Token);
        try
        {
            var sending = cast.LoadMediaFileCommand.ExecuteAsync(file);
            await receiver.WaitForAsync<MediaDataMessage>();

            await receiver.CloseAsync();
            await sending.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await Ended(cast);

            cast.IsMediaPlaying.ShouldBeFalse();
            cast.MediaStatus.ShouldBe("Media was not sent.");
            cast.PairingStatus.ShouldBe("The connection to Living Room was lost.");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task TheTvClosingWhileAFileWasPlaying_SaysSoOnTheMediaPage()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);
        typeof(CastPageViewModel).GetProperty(nameof(CastPageViewModel.IsMediaPlaying))!.SetValue(cast, true);

        await receiver.SendByeAsync();
        await Ended(cast);

        cast.IsMediaPlaying.ShouldBeFalse();
        cast.MediaStatus.ShouldBe("Living Room closed the connection.");
    }

    [Fact]
    public async Task Disconnect_StopsSharingFirst_ThenSaysGoodbye()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver, new IdleMirrorEngine());
        var sharing = cast.StartScreenSessionCommand.ExecuteAsync(null);
        await receiver.WaitForAsync<VideoConfigMessage>();

        await cast.DisconnectCommand.ExecuteAsync(null);
        await sharing.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await receiver.WaitForAsync<ByeMessage>();

        cast.IsMirroring.ShouldBeFalse();
        cast.IsConnected.ShouldBeFalse();
        cast.PairingStatus.ShouldBe("Disconnected from Living Room.");
        var sent = receiver.Received.ToList();
        var idle = sent.FindLastIndex(message => message is SurfaceMessage { Mode: SurfaceMode.Idle });
        idle.ShouldBeGreaterThanOrEqualTo(0, "sharing is stopped, which puts the TV back to idle");
        idle.ShouldBeLessThan(sent.FindIndex(message => message is ByeMessage), "before the goodbye");
    }

    [Fact]
    public async Task Disconnect_WithNothingConnected_DoesNothing()
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            new NoRecentAddresses(),
            receiverInstaller: new OfflineReceiverInstaller());

        cast.CanDisconnect.ShouldBeFalse();
        await cast.DisconnectCommand.ExecuteAsync(null);

        cast.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task Disconnect_WithdrawsAnOpenSwitchQuestion()
    {
        await using var receiver = new LoopbackReceiver();
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }));
        await Pair(shell.Cast, receiver);
        var question = shell.SwitchPrompt.AskAsync(SurfaceSwitchCopy.For(TvSurfaceKind.Mirror, TvSurfaceKind.Browser, "Living Room"));

        await shell.Cast.DisconnectCommand.ExecuteAsync(null);

        shell.SwitchPrompt.IsOpen.ShouldBeFalse();
        (await question).ShouldBeFalse("withdrawn, as keeping what the TV shows");
    }

    [Fact]
    public async Task TheUpdatePanelStopsCountingTheSessionAsLive_AfterADrop()
    {
        await using var receiver = new LoopbackReceiver();
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }));
        await Pair(shell.Cast, receiver);
        shell.Cast.IsSessionConnected.ShouldBeTrue();

        await receiver.CloseAsync();
        await Ended(shell.Cast);

        // The update panel's "something is on the TV" check reads exactly these two.
        shell.Cast.IsSessionConnected.ShouldBeFalse();
        shell.Cast.IsMirroring.ShouldBeFalse();
    }

    [Fact]
    public async Task ASessionAlreadyReplaced_EndingLate_ChangesNothing()
    {
        // Pairing again replaces the session; the old one's end was expected and must not undo
        // the new connection.
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);
        await using var stale = await StaleSessionAsync();

        await cast.OnSessionEndedAsync(stale, new CastSessionClosed(CastSessionEnd.ConnectionLost));

        cast.IsConnected.ShouldBeTrue();
        cast.PairingStatus.ShouldBe("Paired and ready to cast.");
    }

    [Theory]
    [InlineData(CastSessionEnd.EndedByTv, "Living Room closed the connection.")]
    [InlineData(CastSessionEnd.ConnectionLost, "The connection to Living Room was lost.")]
    [InlineData(CastSessionEnd.ProtocolError, "Living Room sent something Flint could not read, so the connection was closed.")]
    [InlineData(CastSessionEnd.ClosedByThisPc, "Disconnected from Living Room.")]
    public async Task EachEnd_IsSaidInWords(CastSessionEnd reason, string expected)
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            new NoRecentAddresses(),
            receiverInstaller: new OfflineReceiverInstaller());
        await cast.ProbeCommand.ExecuteAsync(null);

        cast.DescribeEnd(reason).ShouldBe(expected);
    }

    [Theory]
    [InlineData(CastSessionEnd.EndedByTv, "The TV closed the connection.")]
    [InlineData(CastSessionEnd.ConnectionLost, "The connection to the TV was lost.")]
    public void ATvWithNoName_IsTheTv(CastSessionEnd reason, string expected)
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            new NoRecentAddresses(),
            receiverInstaller: new OfflineReceiverInstaller());

        cast.DescribeEnd(reason).ShouldBe(expected);
    }

    [Fact]
    public async Task ATvWithAnEmptyName_IsTheTvToo()
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { FriendlyName = string.Empty }),
            new NoRecentAddresses(),
            receiverInstaller: new OfflineReceiverInstaller());
        await cast.ProbeCommand.ExecuteAsync(null);

        cast.DescribeEnd(CastSessionEnd.EndedByTv).ShouldBe("The TV closed the connection.");
        cast.DescribeEnd(CastSessionEnd.ConnectionLost).ShouldBe("The connection to the TV was lost.");
    }

    [Fact]
    public async Task AReportWithNoDevice_IsTheTv()
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()),
            new NoRecentAddresses(),
            receiverInstaller: new OfflineReceiverInstaller());
        await cast.ProbeCommand.ExecuteAsync(null);
        typeof(CastPageViewModel).GetProperty(nameof(CastPageViewModel.Report))!
            .SetValue(cast, cast.Report! with { Device = null });

        cast.DescribeEnd(CastSessionEnd.EndedByTv).ShouldBe("The TV closed the connection.");
        cast.DescribeEnd(CastSessionEnd.ProtocolError).ShouldStartWith("The TV sent something");
        cast.DescribeEnd(CastSessionEnd.ConnectionLost).ShouldBe("The connection to the TV was lost.");
    }

    [Fact]
    public async Task TheTvGoing_UnderACoordinatorThatAsksNothing_StillPutsThePageBack()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);
        _ = new ModeSessionCoordinator(cast, BrowserFixtures.ViewModel());

        await receiver.SendByeAsync();
        await Ended(cast);

        cast.PairingStatus.ShouldBe("Living Room closed the connection.");
    }

    [Fact]
    public async Task TheTvGoing_WithdrawsAnOpenSwitchQuestion()
    {
        // A question about what the TV should show means nothing once there is no TV.
        await using var receiver = new LoopbackReceiver();
        using var shell = MainWindowViewModel.CreateWith(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }));
        await Pair(shell.Cast, receiver);
        var question = shell.SwitchPrompt.AskAsync(SurfaceSwitchCopy.For(TvSurfaceKind.Mirror, TvSurfaceKind.Browser, "Living Room"));

        await receiver.SendByeAsync();
        await Ended(shell.Cast);

        shell.SwitchPrompt.IsOpen.ShouldBeFalse();
        (await question).ShouldBeFalse();
    }

    [Fact]
    public async Task Disconnect_WithACoordinatorThatAsksNothing_StillDisconnects()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);
        _ = new ModeSessionCoordinator(cast, BrowserFixtures.ViewModel());

        await cast.DisconnectCommand.ExecuteAsync(null);

        cast.IsConnected.ShouldBeFalse();
        await receiver.WaitForAsync<ByeMessage>();
    }

    [AvaloniaFact]
    public async Task TheDisconnectButton_ShowsOnlyWhileConnected()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);
        var page = new CastPage { DataContext = cast };
        var window = new Window { Width = 1024, Height = 900, Content = page };
        window.Show();
        try
        {
            window.UpdateLayout();
            DisconnectButton(page).IsEffectivelyVisible.ShouldBeTrue();

            await receiver.SendByeAsync();
            await Ended(cast);
            window.UpdateLayout();

            DisconnectButton(page).IsEffectivelyVisible.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CastPage_Connected()
    {
        await using var receiver = new LoopbackReceiver();
        var cast = await PairedAsync(receiver);

        Snapshot.Matches("cast-page-connected", new CastPage { DataContext = cast });
    }

    private static Button DisconnectButton(Control page) =>
        page.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "DISCONNECT"));

    /// <summary>Waits until the page has finished handling a session that ended.</summary>
    /// <remarks>
    /// The Cast page's status line is the handler's last write, so once it no longer says the
    /// session is ready, every other part of the page has been updated too.
    /// </remarks>
    private static Task Ended(CastPageViewModel cast) =>
        Until(() => !cast.IsConnected && cast.PairingStatus != "Paired and ready to cast.");

    private static async Task<CastSession> StaleSessionAsync()
    {
        await using var other = new LoopbackReceiver();
        return await CastSession.ConnectAsync(IPAddress.Loopback, other.Port, "123456", cancellationToken: Token);
    }

    /// <summary>An engine whose screen never changes: it starts, then only reports idle ticks.</summary>
    private sealed class IdleMirrorEngine : IMirrorEngine
    {
        public IMirrorEngineSession Start(MirrorSessionOptions options) => new IdleSession();

        private sealed class IdleSession : IMirrorEngineSession
        {
            public VideoCodec Codec => VideoCodec.H264;

            public MirrorEncoderKind EncoderKind => MirrorEncoderKind.Hardware;

            public int Width => 1280;

            public int Height => 720;

            public IReadOnlyList<byte[]> CodecSpecificData { get; } = [[0, 0, 0, 1, 0x67], [0, 0, 0, 1, 0x68]];

            public MirrorTick Next(Span<byte> buffer) => MirrorTick.Nothing;

            public void RequestKeyFrame()
            {
            }

            public MirrorSessionStats ReadStats() => default;

            public void Dispose()
            {
            }
        }
    }
}
