using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>This PC's sound beside a screen share: started, stopped, muted and put back.</summary>
public sealed class CastPageSoundTests : IDisposable
{
    private static readonly AudioDevice Speakers = new("speakers", "Speakers", true);
    private static readonly AudioDevice Usb = new("usb", "USB headset", false);

    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly FakeAudio audio = new(Speakers, Usb);
    private readonly InMemorySoundMemory memory = new();
    private readonly ManualTime clock = new();

    public void Dispose() => settings.Dispose();

    [AvaloniaFact]
    public async Task TvOnly_MutesThisPcOnceSoundIsGoing_AndPutsItBackWhenSharingStops()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);

        var sharing = await SharingAsync(cast, tv);
        await tv.WaitForAsync<AudioConfigMessage>();
        await Until(() => cast.IsMutingThisPc);

        audio.Muted("speakers").ShouldBeTrue();
        memory.PendingRestore.ShouldBe(new MuteToRestore("speakers", false));
        cast.SoundState.ShouldBe(AudioShareState.Ready);
        cast.IsSharingSound.ShouldBeTrue();
        var started = audio.Started.ShouldHaveSingleItem();
        started.StartOffsetUs.ShouldBeGreaterThanOrEqualTo(0, "timed from the picture's own clock");
        (started with { StartOffsetUs = 0 }).ShouldBe(new AudioShareOptions(null, 128, 0, 0));
        await StopAsync(cast, sharing);

        audio.Muted("speakers").ShouldBeFalse();
        memory.PendingRestore.ShouldBeNull();
        cast.IsMutingThisPc.ShouldBeFalse();
        cast.SoundState.ShouldBe(AudioShareState.Off);
        cast.IsSharingSound.ShouldBeFalse();
        audio.Share!.Disposed.ShouldBeTrue();
    }

    [AvaloniaFact]
    public async Task SoundOff_StartsNoSound_AndTheTvAndPc_MutesNothing()
    {
        Screen(screen => screen with { ShareSound = false });
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);

        audio.Started.ShouldBeEmpty();
        cast.SoundState.ShouldBe(AudioShareState.Off);
        Screen(screen => screen with { ShareSound = true, SoundDestination = SoundDestination.TvAndPc });
        await Until(() => audio.Share is not null && cast.SoundState is AudioShareState.Ready);
        await StopAsync(cast, sharing);

        audio.MuteCalls.ShouldBeEmpty();
    }

    [AvaloniaFact]
    public async Task SoundThatCannotStart_SaysWhy_AndThePictureCarriesOn()
    {
        audio.Refusal = new AudioEngineException("This edition of Windows has no AAC encoder.", AudioStartFailure.NoEncoder);
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);

        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.SoundState is AudioShareState.Unavailable);

        cast.SoundProblem.ShouldBe("This edition of Windows has no AAC encoder.");
        cast.IsMirroring.ShouldBeTrue();
        cast.Failure.ShouldBeNull();
        audio.MuteCalls.ShouldBeEmpty("nothing to hear, so nothing to mute");
        await StopAsync(cast, sharing);
        cast.SoundState.ShouldBe(AudioShareState.Unavailable, "the reason stays on the page after the share");
    }

    [AvaloniaFact]
    public async Task TheTvGoingAway_EndsSound_AndPutsThisPcBack()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);

        await tv.CloseAsync();
        await Until(() => !cast.IsSharingSound);

        audio.Muted("speakers").ShouldBeFalse();
        cast.SoundState.ShouldBe(AudioShareState.Off);
        await sharing;
    }

    [AvaloniaFact]
    public async Task APause_StopsSound_AndPutsThisPcBack_UntilResume()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);

        cast.PauseMirror(MirrorPause.HoldingLastPicture).ShouldBeTrue();
        audio.Muted("speakers").ShouldBeFalse();
        cast.IsMutingThisPc.ShouldBeFalse();
        await Until(() => audio.Share!.Pauses.Contains(true));

        cast.ResumeMirror();
        audio.Muted("speakers").ShouldBeTrue();
        cast.IsMutingThisPc.ShouldBeTrue();
        await Until(() => audio.Share!.Pauses.Contains(false));
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task ASharePausedBeforeSoundIsGoing_MutesOnlyOnResume()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        Screen(screen => screen with { ShareSound = false });
        var sharing = await SharingAsync(cast, tv);
        cast.PauseMirror(MirrorPause.Black);

        Screen(screen => screen with { ShareSound = true });
        await Until(() => audio.Share is not null && cast.SoundState is AudioShareState.Ready);
        await Task.Delay(400, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
        audio.MuteCalls.ShouldBeEmpty();

        cast.ResumeMirror();
        await Until(() => cast.IsMutingThisPc);
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task SwitchingSoundOffAndOnMidShare_TouchesOnlySound()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);
        var first = audio.Share!;

        Screen(screen => screen with { ShareSound = false });
        await Until(() => !cast.IsSharingSound);
        first.Disposed.ShouldBeTrue();
        audio.Muted("speakers").ShouldBeFalse();
        cast.IsMirroring.ShouldBeTrue();
        Screen(screen => screen with { SoundKbps = 96 });
        cast.IsSharingSound.ShouldBeFalse("switched off stays off, whatever else changes");

        Screen(screen => screen with { ShareSound = true });
        await Until(() => cast.IsMutingThisPc);
        audio.Share.ShouldNotBeSameAs(first);
        cast.IsMirroring.ShouldBeTrue();
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task ChangingWhereSoundComesFromOrItsQuality_RestartsSound_AndTheDelayChangesInPlace()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);

        Screen(screen => screen with { SoundDelayMilliseconds = 120 });
        await Until(() => audio.Share!.Delays.Contains(120));
        audio.Started.Count.ShouldBe(1, "a delay is changed in place");

        Screen(screen => screen with { SoundKbps = 192 });
        await Until(() => audio.Started.Count == 2 && cast.IsMutingThisPc);
        Screen(screen => screen with { SoundSource = SoundSource.NamedDevice, SoundDeviceIdentity = "usb" });
        await Until(() => audio.Started.Count == 3 && cast.IsMutingThisPc);

        (audio.Started.Last() with { StartOffsetUs = 0 }).ShouldBe(new AudioShareOptions("usb", 192, 0, 120));
        audio.Muted("usb").ShouldBeTrue("the chosen output is the one muted");
        audio.Muted("speakers").ShouldBeFalse();
        Screen(screen => screen with { SoundDeviceIdentity = "usb" });
        audio.Started.Count.ShouldBe(3, "nothing changed");
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task ChangingWhereSoundPlays_MutesOrPutsBackAtOnce()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);

        Screen(screen => screen with { SoundDestination = SoundDestination.TvAndPc });
        audio.Muted("speakers").ShouldBeFalse();
        cast.IsMutingThisPc.ShouldBeFalse();

        Screen(screen => screen with { SoundDestination = SoundDestination.TvOnly });
        audio.Muted("speakers").ShouldBeTrue();
        cast.IsMutingThisPc.ShouldBeTrue();
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task AMuteThatSilencesCapture_IsUndone_Explained_AndRemembered()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);
        var reports = 0;
        cast.SoundReported += _ => reports++;

        audio.Share!.Stats = new AudioShareStats(10, 0, 0, AudioShareState.Ready, 0.3f);
        await Until(() => reports > 0);
        clock.Advance(TvOnlyMute.SilenceProof);
        await Until(() => cast.SoundNotice is not null);

        cast.SoundNotice.ShouldBe("Muting this PC silenced the sound Flint shares, so this output plays on the TV and this PC from now on.");
        cast.IsMutingThisPc.ShouldBeFalse();
        audio.Muted("speakers").ShouldBeFalse();
        memory.MuteSilences("speakers").ShouldBeTrue();
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task ThePersonUnmuting_IsRespected_ForTheRestOfTheShare()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);

        audio.PersonSets("speakers", false);
        await Until(() => cast.SoundNotice is not null);

        cast.SoundNotice.ShouldBe("You turned this PC's sound back on, so Flint leaves it on for the rest of this share.");
        cast.PauseMirror(MirrorPause.HoldingLastPicture);
        cast.ResumeMirror();
        audio.Muted("speakers").ShouldBeFalse();
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task FlintClosing_PutsThisPcBack_AtOnce()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.IsMutingThisPc);

        cast.EndSound();

        audio.Muted("speakers").ShouldBeFalse();
        cast.IsMutingThisPc.ShouldBeFalse();
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task WithNoOutputToMute_SoundStillGoes()
    {
        audio.Outputs.Clear();
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => cast.SoundState is AudioShareState.Ready && audio.Share is not null);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();

        audio.MuteCalls.ShouldBeEmpty();
        cast.IsMutingThisPc.ShouldBeFalse();
        cast.SoundOutputs().ShouldBeEmpty();
        await StopAsync(cast, sharing);
    }

    [AvaloniaFact]
    public async Task TheTvVolume_IsSent_AndATvThatHasGoneIsNoFailure()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);

        await cast.SetTvVolumeAsync(0.4f);
        await tv.WaitUntilAsync(received => received.OfType<ControlMessage>().Any(control => control.Event is VolumeControl { Level: 0.4f }));

        await cast.DisconnectCommand.ExecuteAsync(null);
        await Should.NotThrowAsync(() => cast.SetTvVolumeAsync(0.5f));
        cast.SoundOutputs().ShouldBe([Speakers, Usb]);
    }

    [AvaloniaFact]
    public async Task ThePointerSetting_IsAppliedAtTheStart_AndAtOnceWhenItChanges()
    {
        Screen(screen => screen with { ShowPointer = false, ShareSound = false });
        await using var tv = new LoopbackReceiver();
        var mirror = new RecordingMirrorEngine();
        var cast = new CastPageViewModel(
            BrowserFixtures.Prober(BrowserFixtures.EligibleDevice() with { Address = System.Net.IPAddress.Loopback }),
            new NoRecentAddresses(),
            mirrorEngine: mirror,
            receiverInstaller: new OfflineReceiverInstaller(),
            time: clock);
        cast.UseReconnect(new InMemoryKnownTvStore(), settings);
        await Pair(cast, tv);
        var sharing = await SharingAsync(cast, tv);
        await Until(() => mirror.Pointer.Count == 1);

        Screen(screen => screen with { ShowPointer = true });
        await Until(() => mirror.Pointer.Count == 2);
        await StopAsync(cast, sharing);
        Screen(screen => screen with { ShowPointer = false });

        mirror.Pointer.ToArray().ShouldBe([false, true], "and nothing once the share has stopped");
    }

    [AvaloniaFact]
    public async Task StartingSound_WithNoShareRunning_DoesNothing()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);

        cast.StartSound();
        await cast.StopSoundAsync();

        audio.Started.ShouldBeEmpty();
        cast.SoundState.ShouldBe(AudioShareState.Off);
    }

    [AvaloniaFact]
    public async Task StoppingTheShareWithTheTvsRemote_StopsItHere_WithItsSound_AndSaysSo()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);
        await tv.WaitForAsync<AudioConfigMessage>();
        await Until(() => cast.IsMutingThisPc);

        await tv.SendAsync(new ControlMessage(1, new TransportControl(TransportAction.Stop)));
        await sharing;

        cast.IsMirroring.ShouldBeFalse();
        cast.MirrorStatus.ShouldBe("Sharing stopped on the TV.");
        cast.Failure.ShouldBeNull("the person ended it; nothing went wrong");
        cast.IsSharingSound.ShouldBeFalse();
        audio.Muted("speakers").ShouldBeFalse("this PC's sound is put back");
        cast.IsSessionConnected.ShouldBeTrue("the TV is still connected, ready for the next share");
    }

    [AvaloniaFact]
    public async Task AStopFromTheTv_ForAShareThatHasEnded_StopsNothing()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);
        var sharing = await SharingAsync(cast, tv);

        using var ended = new CancellationTokenSource();
        cast.StopForTv(ended);

        cast.IsMirroring.ShouldBeTrue("a stop meant for another share does not end this one");
        ended.IsCancellationRequested.ShouldBeFalse();
        await StopAsync(cast, sharing);
        cast.MirrorStatus.ShouldNotBe("Sharing stopped on the TV.");
    }

    private Task<CastPageViewModel> PairedAsync(LoopbackReceiver tv) =>
        PairedWithSoundAsync(tv, settings, audio, memory, clock);

    private static async Task<Task> SharingAsync(CastPageViewModel cast, LoopbackReceiver tv)
    {
        var sharing = cast.StartScreenSessionCommand.ExecuteAsync(null);
        await tv.WaitForAsync<VideoConfigMessage>();
        return sharing;
    }

    private static async Task StopAsync(CastPageViewModel cast, Task sharing)
    {
        await cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
    }

    private void Screen(Func<ScreenSettings, ScreenSettings> change) =>
        settings.Update(current => current with { Screen = change(current.Screen) });

    [AvaloniaFact]
    public async Task AReportFromAShareSinceReplaced_IsIgnored()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedAsync(tv);

        cast.OnSoundReported(new Flint.Session.AudioPump(audio), new Flint.Session.AudioPumpReport(new AudioShareStats(1, 0, 0.5f, AudioShareState.Sounding), null));

        cast.SoundState.ShouldBe(AudioShareState.Off);
        cast.SoundLevel.ShouldBe(0);
    }
}
