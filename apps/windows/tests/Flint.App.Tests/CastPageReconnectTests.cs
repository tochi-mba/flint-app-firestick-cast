using System.Net;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>
/// Reaching the TV again with the login it granted at pairing: as Flint starts, and when the
/// connection drops, against a loopback TV that grants and checks logins as a real one does.
/// </summary>
public sealed partial class CastPageReconnectTests : IDisposable
{
    private static readonly string Login = new('k', 43);

    private readonly ManualTime clock = new();
    private readonly InMemoryKnownTvStore store = new();
    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly string film = Path.Combine(Path.GetTempPath(), $"flint-reconnect-{Guid.NewGuid():N}.mp4");

    public CastPageReconnectTests() => File.WriteAllBytes(film, new byte[64]);

    public void Dispose()
    {
        settings.Dispose();
        File.Delete(film);
    }

    [Fact]
    public async Task Pairing_RemembersTheTv_ByTheNameItGives_WithItsLogin()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };

        var cast = await PairedPageAsync(tv);

        var known = cast.KnownTvs.ShouldHaveSingleItem();
        known.Name.ShouldBe("Living Room");
        known.Address.ShouldBe("127.0.0.1");
        known.ReceiverPort.ShouldBe(tv.Port);
        known.HasLogin.ShouldBeTrue();
        known.ToString().ShouldNotContain(Login, Case.Sensitive, "a login never reaches a log through a TV's text");
    }

    [Fact]
    public async Task ADrop_IsReconnectedWithTheLogin_NoCodeAsked()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);

        await tv.CloseAsync();
        await Until(() => cast.IsReconnecting);
        cast.ReconnectBanner.ShouldBe("Reconnecting to Living Room. Trying again in 1 s.");
        cast.CancelReconnectCommand.CanExecute(null).ShouldBeTrue();

        await RunFirstAttemptAsync(cast);
        cast.IsSessionConnected.ShouldBeTrue();
        tv.LoginsAccepted.ShouldBe(1);
        cast.PairingStatus.ShouldBe("Connected to Living Room again.");
        cast.HasReconnectBanner.ShouldBeFalse("nothing was on the TV, so there is nothing to offer");
        cast.IsReconnecting.ShouldBeFalse();
    }

    [Fact]
    public async Task AFileThatWasPlaying_IsOffered_AndAcceptingPlaysItFromWhereItWas()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login, AnswerLoad = LoopbackReceiver.PlaysEverything };
        var cast = await PairedPageAsync(tv);
        await PlayFilmAsync(cast, tv);

        await DropAndReconnectAsync(cast, tv);

        cast.ReconnectBanner.ShouldBe($"Connected again. Play {Path.GetFileName(film)} from 12:34?");
        cast.ReconnectOfferAction.ShouldBe("PLAY");
        await cast.AcceptReconnectOfferCommand.ExecuteAsync(null);
        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].StartPositionMs.ShouldBe(754_000);
        cast.HasReconnectBanner.ShouldBeFalse();
    }

    [Fact]
    public async Task CarryOn_PlaysTheFileAgainWithoutAsking()
    {
        settings.Update(current => current with { General = current.General with { AfterReconnect = ReconnectOutcome.CarryOn } });
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login, AnswerLoad = LoopbackReceiver.PlaysEverything };
        var cast = await PairedPageAsync(tv);
        await PlayFilmAsync(cast, tv);

        await DropAndReconnectAsync(cast, tv);

        await Until(() => tv.Loads.Count == 2);
        tv.Loads[1].StartPositionMs.ShouldBe(754_000);
        cast.HasReconnectOffer.ShouldBeFalse();
    }

    [Fact]
    public async Task DoNothing_ReconnectsAndLeavesTheTvAlone()
    {
        settings.Update(current => current with { General = current.General with { AfterReconnect = ReconnectOutcome.DoNothing } });
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login, AnswerLoad = LoopbackReceiver.PlaysEverything };
        var cast = await PairedPageAsync(tv);
        await PlayFilmAsync(cast, tv);

        await DropAndReconnectAsync(cast, tv);

        tv.Loads.Count.ShouldBe(1);
        cast.HasReconnectBanner.ShouldBeFalse();
    }

    [Fact]
    public async Task DismissingTheOffer_LeavesTheTvAlone()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login, AnswerLoad = LoopbackReceiver.PlaysEverything };
        var cast = await PairedPageAsync(tv);
        await PlayFilmAsync(cast, tv);
        await DropAndReconnectAsync(cast, tv);

        cast.DismissReconnectOfferCommand.Execute(null);
        await cast.AcceptReconnectOfferCommand.ExecuteAsync(null);

        cast.HasReconnectBanner.ShouldBeFalse();
        tv.Loads.Count.ShouldBe(1, "accepting after dismissing has nothing left to play");
    }

    [Fact]
    public async Task AScreenShare_IsOffered_AndCarryingOnCountsDownWhereItCanBeStopped()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv, new IdleMirrorEngine());

        cast.NoteWhatWasOnTheTv(wasMirroring: true, wasPlaying: false);
        await cast.OfferWhatWasOnTheTvAsync();
        cast.ReconnectBanner.ShouldBe("Connected again. Share your screen again?");
        cast.ReconnectOfferAction.ShouldBe("SHARE AGAIN");

        var sharing = cast.AcceptReconnectOfferCommand.ExecuteAsync(null);
        await Until(() => cast.ReconnectBanner == "Sharing your screen again in 3...");
        await AdvanceUntilAsync(() => cast.ReconnectBanner == "Sharing your screen again in 2...");
        cast.CancelReconnectCommand.Execute(null);
        await sharing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        cast.IsMirroring.ShouldBeFalse("a share stopped during its countdown never starts");
        cast.HasReconnectBanner.ShouldBeFalse();
    }

    [Fact]
    public async Task CarryingOn_StartsTheShare_OnceTheCountdownEnds()
    {
        settings.Update(current => current with { General = current.General with { AfterReconnect = ReconnectOutcome.CarryOn } });
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv, new IdleMirrorEngine());
        cast.NoteWhatWasOnTheTv(wasMirroring: true, wasPlaying: false);

        var offering = cast.OfferWhatWasOnTheTvAsync();
        await Until(() => cast.ReconnectBanner == "Sharing your screen again in 3...");
        await AdvanceUntilAsync(() => cast.IsMirroring);
        cast.HasReconnectBanner.ShouldBeFalse();
        await cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await offering.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ARefusedLogin_IsForgotten_AndTheTvsAppIsOpenedForItsCode()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var launcher = new RecordingLauncher();
        var cast = await PairedPageAsync(tv, launcher: launcher);
        tv.RefusesLogins = true;

        await tv.CloseAsync();
        await Until(() => cast.IsReconnecting);
        await RunFirstAttemptAsync(cast);

        tv.LoginsRefused.ShouldBe(1);
        cast.IsSessionConnected.ShouldBeFalse();
        cast.KnownTvs.ShouldHaveSingleItem().HasLogin.ShouldBeFalse();
        cast.ReconnectBanner.ShouldBe("Living Room needs a new code. Its app restarted, or someone asked it for a new one.");
        launcher.Launches.ShouldBe(1);
        cast.PairingStatus.ShouldBe("The receiver is open on the TV. Enter the six-digit code shown there, then pair.");
    }

    [Theory]
    [InlineData(false, false, "Enter the six-digit code shown on the TV, then pair.")]
    [InlineData(true, true, "Open Flint on the TV, enter the six-digit code shown there, then pair.")]
    public async Task ARefusedLogin_AsksForTheCode_WhenTheTvsAppIsNotOpened_OrCannotBe(bool openSetting, bool launchFails, string status)
    {
        settings.Update(current => current with { General = current.General with { OpenReceiverForNewCode = openSetting } });
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var launcher = new RecordingLauncher { Fails = launchFails };
        var cast = await PairedPageAsync(tv, launcher: launcher);
        tv.RefusesLogins = true;

        await tv.CloseAsync();
        await Until(() => cast.IsReconnecting);
        await RunFirstAttemptAsync(cast);

        cast.PairingStatus.ShouldBe(status);
        launcher.Launches.ShouldBe(openSetting ? 1 : 0);
    }

    [Fact]
    public async Task ATvThatNeverComesBack_IsGivenUpOn_WhenTheTimeIsUp()
    {
        settings.Update(current => current with { General = current.General with { ReconnectSeconds = 30 } });
        var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);

        await tv.CloseAsync();
        await tv.DisposeAsync();
        await Until(() => cast.IsReconnecting);
        for (var step = 0; step < 12 && !cast.ReconnectTask.IsCompleted; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(15));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        await cast.ReconnectTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        cast.ReconnectBanner.ShouldBe("Could not reach Living Room again. Connect on the Cast page when it is back.");
        cast.IsReconnecting.ShouldBeFalse();
    }

    [Fact]
    public async Task CancellingTheReconnect_StopsTrying()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);

        await tv.CloseAsync();
        await Until(() => cast.IsReconnecting);
        cast.CancelReconnectCommand.Execute(null);
        await cast.ReconnectTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        cast.HasReconnectBanner.ShouldBeFalse();
        tv.LoginsAccepted.ShouldBe(0);
    }

    [Fact]
    public async Task AfterThePersonDisconnects_NothingReconnects()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);

        await cast.DisconnectCommand.ExecuteAsync(null);
        await cast.KeepTryingAsync(TestContext.Current.CancellationToken);

        cast.IsReconnecting.ShouldBeFalse();
        cast.HasReconnectBanner.ShouldBeFalse();
        tv.LoginsAccepted.ShouldBe(0);
    }

    [Fact]
    public async Task WithKeepTryingOff_ADropIsLeftAsItIs()
    {
        settings.Update(current => current with { General = current.General with { ReconnectAfterDrop = false } });
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);

        await tv.CloseAsync();
        await Until(() => !cast.IsSessionConnected);
        await cast.ReconnectTask;

        cast.IsReconnecting.ShouldBeFalse();
        tv.LoginsAccepted.ShouldBe(0);
    }

    [Fact]
    public async Task AsFlintStarts_TheLastTvIsReachedWithItsLogin()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        store.Save(new KnownTv("Living Room", "127.0.0.1", tv.Port, Login, clock.GetUtcNow()));
        var cast = Page();

        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        cast.IsSessionConnected.ShouldBeTrue();
        tv.LoginsAccepted.ShouldBe(1);
        cast.HasReconnectBanner.ShouldBeFalse();
        cast.PairingStatus.ShouldBe("Connected to Living Room again.");
    }

    [Fact]
    public async Task AsFlintStarts_ADifferentTvAtTheAddress_IsNeverSentTheLogin()
    {
        await using var tv = new LoopbackReceiver { Name = "Bedroom", Login = Login };
        store.Save(new KnownTv("Living Room", "127.0.0.1", tv.Port, Login, clock.GetUtcNow()));
        var cast = Page();

        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

        cast.IsSessionConnected.ShouldBeFalse();
        tv.LoginsAccepted.ShouldBe(0);
        tv.LoginsRefused.ShouldBe(0, "the login was never presented");
        cast.HasReconnectBanner.ShouldBeFalse();
    }

    [Fact]
    public async Task AsFlintStarts_NothingIsTried_WhenTheSettingIsOff_OrNoTvHasALogin()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = Page();
        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeFalse("nothing is remembered");

        store.Save(new KnownTv("Living Room", "127.0.0.1", tv.Port, Token: null, clock.GetUtcNow()));
        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeFalse("its login is gone");

        store.Save(new KnownTv("Living Room", "127.0.0.1", tv.Port, Login, clock.GetUtcNow()));
        settings.Update(current => current with { General = current.General with { ReconnectOnStart = false } });
        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeFalse("the setting is off");

        tv.LoginsAccepted.ShouldBe(0);
    }

    [Fact]
    public async Task APageNotWiredForReconnecting_NeverReconnects()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedAsync(tv, time: clock);

        (await cast.ReconnectOnStartAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        await tv.CloseAsync();
        await Until(() => !cast.IsSessionConnected);
        await cast.ReconnectTask;

        cast.IsReconnecting.ShouldBeFalse();
    }

    [Fact]
    public async Task ForgettingTvs_RemovesThemAndTheirLogins()
    {
        await using var tv = new LoopbackReceiver { Name = "Living Room", Login = Login };
        var cast = await PairedPageAsync(tv);
        store.Save(new KnownTv("Bedroom", "10.0.0.9", 47855, Login, clock.GetUtcNow().AddDays(-1)));

        cast.ForgetTv("Living Room");
        cast.KnownTvs.ShouldHaveSingleItem().Name.ShouldBe("Bedroom");
        cast.ForgetAllTvs();
        cast.KnownTvs.ShouldBeEmpty();
    }

    private CastPageViewModel Page(IMirrorEngine? engine = null, IReceiverLauncher? launcher = null)
    {
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = IPAddress.Loopback }),
            new NoRecentAddresses(),
            receiverLauncher: launcher ?? new RecordingLauncher(),
            mirrorEngine: engine,
            receiverInstaller: new OfflineReceiverInstaller(),
            time: clock);
        cast.UseReconnect(store, settings);
        return cast;
    }

    private async Task<CastPageViewModel> PairedPageAsync(LoopbackReceiver tv, IMirrorEngine? engine = null, IReceiverLauncher? launcher = null)
    {
        var cast = Page(engine, launcher);
        await Pair(cast, tv);
        return cast;
    }

    private async Task PlayFilmAsync(CastPageViewModel cast, LoopbackReceiver tv)
    {
        (await cast.PlayFileAsync(film, 0, MediaTakeover.Ask)).ShouldBe(MediaStart.Playing);
        await tv.SendAsync(new PlaybackStateMessage(PlaybackState.Playing, 754_000, 5_520_000));
        await Until(() => cast.NowPlaying.PositionMs >= 754_000);
    }

    /// <summary>Moves the clock on until the attempt under way has finished.</summary>
    /// <remarks>
    /// The page says it is reconnecting a moment before it sets its first timer. Moving the clock
    /// once, in that moment, would leave the timer waiting for a time that has already passed, so
    /// the clock is nudged on a quarter of a second at a time until the attempt is over.
    /// </remarks>
    private async Task RunFirstAttemptAsync(CastPageViewModel cast)
    {
        await AdvanceUntilAsync(() => cast.ReconnectTask.IsCompleted);
        await cast.ReconnectTask.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    /// <summary>Moves the clock on a quarter of a second at a time until <paramref name="done"/> holds.</summary>
    private async Task AdvanceUntilAsync(Func<bool> done)
    {
        for (var step = 0; step < 200 && !done(); step++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        done().ShouldBeTrue("the clock moved on fifty seconds and it still had not happened");
    }

    private async Task DropAndReconnectAsync(CastPageViewModel cast, LoopbackReceiver tv)
    {
        await tv.CloseAsync();
        await Until(() => cast.IsReconnecting);
        await RunFirstAttemptAsync(cast);
        cast.IsSessionConnected.ShouldBeTrue();
    }

    /// <summary>Opens nothing on any TV, and counts how often it was asked to.</summary>
    private sealed class RecordingLauncher : IReceiverLauncher
    {
        public int Launches { get; private set; }

        public bool Fails { get; init; }

        public Task LaunchAsync(FireTvDevice device, string packageName, CancellationToken cancellationToken = default)
        {
            Launches++;
            return Fails ? Task.FromException(new IOException("ADB is not answering.")) : Task.CompletedTask;
        }
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

            public void SetPause(MirrorPause pause)
            {
            }

            public MirrorSessionStats ReadStats() => default;

            public void Dispose()
            {
            }
        }
    }
}
