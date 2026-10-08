using Flint.App.Services;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>"TV only": muting this PC for a share and putting it back exactly as it was.</summary>
public sealed class TvOnlyMuteTests
{
    private static readonly AudioDevice Speakers = new("speakers", "Speakers", true);
    private static readonly AudioDevice Usb = new("usb", "USB headset", false);

    private static readonly AudioShareStats Sounding = new(10, 0, 0.05f, AudioShareState.Sounding, 0.2f);
    private static readonly AudioShareStats SilencedByMute = new(10, 0, 0, AudioShareState.Ready, 0.2f);

    private readonly FakeAudio audio = new(Speakers, Usb);
    private readonly InMemorySoundMemory memory = new();
    private readonly ManualTime clock = new();

    [Fact]
    public void Beginning_WritesWhatToPutBackFirst_ThenMutes_AndEndingPutsItBack()
    {
        var mute = new TvOnlyMute(audio, new WatchedMemory(memory, audio), clock);

        mute.Begin("speakers").ShouldBeTrue();

        mute.IsMuting.ShouldBeTrue();
        audio.Muted("speakers").ShouldBeTrue();
        memory.PendingRestore.ShouldBe(new MuteToRestore("speakers", false));
        mute.End();
        audio.Muted("speakers").ShouldBeFalse();
        memory.PendingRestore.ShouldBeNull();
        mute.IsMuting.ShouldBeFalse();
        audio.MuteCalls.ShouldBe(["speakers=True", "speakers=False"], ignoreOrder: false, customMessage: "only the mute is touched, once each way");
    }

    [Fact]
    public void AnOutputThatWasAlreadyMuted_StaysMuted()
    {
        audio.PersonSets("usb", true);
        var mute = new TvOnlyMute(audio, memory, clock);

        mute.Begin("usb").ShouldBeTrue();
        mute.End();

        audio.Muted("usb").ShouldBeTrue();
        memory.PendingRestore.ShouldBeNull();
    }

    [Fact]
    public void APause_PutsTheOutputBack_AndResumingMutesAgain()
    {
        var mute = new TvOnlyMute(audio, memory, clock);
        mute.Begin("speakers");

        mute.Suspend();
        audio.Muted("speakers").ShouldBeFalse();
        memory.PendingRestore.ShouldBeNull();
        mute.IsMuting.ShouldBeFalse();
        mute.Resume().ShouldBeTrue();

        audio.Muted("speakers").ShouldBeTrue();
        mute.Observe(Sounding).ShouldBe(MuteNews.None);
    }

    [Fact]
    public void ThePersonTurningSoundBackOn_IsRespectedForTheRestOfTheShare()
    {
        var mute = new TvOnlyMute(audio, memory, clock);
        mute.Begin("speakers");

        audio.PersonSets("speakers", false);
        mute.Observe(Sounding).ShouldBe(MuteNews.PersonUnmuted);

        mute.IsMuting.ShouldBeFalse();
        memory.PendingRestore.ShouldBeNull("nothing of Flint's is left to put back");
        mute.Observe(Sounding).ShouldBe(MuteNews.None);
        mute.Suspend();
        mute.Resume().ShouldBeFalse("not muted again after a pause either");
        audio.Muted("speakers").ShouldBeFalse();
        mute.End();
        audio.MuteCalls.ShouldBe(["speakers=True"], ignoreOrder: false, customMessage: "and nothing is put back over the person's choice");
    }

    [Fact]
    public void AMuteThatSilencesCapture_IsFoundAfterTwoSeconds_UndoneAndRemembered()
    {
        var mute = new TvOnlyMute(audio, memory, clock);
        mute.Begin("usb");

        mute.Observe(SilencedByMute).ShouldBe(MuteNews.None);
        clock.Advance(TvOnlyMute.SilenceProof - TimeSpan.FromMilliseconds(1));
        mute.Observe(SilencedByMute).ShouldBe(MuteNews.None, "not yet two seconds");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        mute.Observe(SilencedByMute).ShouldBe(MuteNews.MuteSilencesCapture);

        audio.Muted("usb").ShouldBeFalse();
        memory.MuteSilences("usb").ShouldBeTrue();
        mute.IsMuting.ShouldBeFalse();
        mute.Begin("usb").ShouldBeFalse("remembered: that output is never muted again");
        audio.Muted("usb").ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.005f, 0f)]
    [InlineData(0.2f, 0.002f)]
    public void QuietOrHeardSound_IsNeverBlamedOnTheMute(float meter, float level)
    {
        var mute = new TvOnlyMute(audio, memory, clock);
        mute.Begin("speakers");

        mute.Observe(SilencedByMute).ShouldBe(MuteNews.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        mute.Observe(new AudioShareStats(10, 0, level, AudioShareState.Ready, meter)).ShouldBe(MuteNews.None);
        clock.Advance(TimeSpan.FromSeconds(1.5));
        mute.Observe(SilencedByMute).ShouldBe(MuteNews.None, "the silent spell started again");

        mute.IsMuting.ShouldBeTrue();
    }

    [Fact]
    public void AnOutputWhoseMuteCannotBeRead_IsLeftAlone()
    {
        audio.HidesMute = true;
        var mute = new TvOnlyMute(audio, memory, clock);

        mute.Begin("speakers").ShouldBeFalse();

        memory.PendingRestore.ShouldBeNull();
        audio.MuteCalls.ShouldBeEmpty();
    }

    [Fact]
    public void AMuteWindowsRefuses_LeavesNothingMuted_AndKeepsTheRecordForNextLaunch()
    {
        audio.RefusesMute = true;
        var mute = new TvOnlyMute(audio, memory, clock);

        mute.Begin("speakers").ShouldBeFalse();

        mute.IsMuting.ShouldBeFalse();
        mute.Observe(SilencedByMute).ShouldBe(MuteNews.None);
        memory.PendingRestore.ShouldBe(new MuteToRestore("speakers", false), "harmless: putting it back sets it as it already is");
    }

    [Fact]
    public void AnOutputThatCannotBePutBack_StaysOnRecord_AndAShareLaterThisRunKnowsHowItWas()
    {
        var mute = new TvOnlyMute(audio, memory, clock);
        mute.Begin("speakers");
        audio.RefusesMute = true;

        mute.End();

        memory.PendingRestore.ShouldBe(new MuteToRestore("speakers", false));
        audio.RefusesMute = false;
        mute.Begin("speakers").ShouldBeTrue();
        memory.PendingRestore.ShouldBe(new MuteToRestore("speakers", false), "not \"muted\", which is only how Flint left it");
        mute.End();
        audio.Muted("speakers").ShouldBeFalse();
    }

    [Fact]
    public void BeginningAgain_PutsTheLastOutputBackFirst()
    {
        var mute = new TvOnlyMute(audio, memory, clock);
        mute.Begin("speakers");

        mute.Begin("usb").ShouldBeTrue();

        audio.Muted("speakers").ShouldBeFalse();
        audio.Muted("usb").ShouldBeTrue();
        Should.Throw<ArgumentException>(() => mute.Begin(" "));
    }

    [Fact]
    public void ALeftOverMute_IsPutBackAtLaunch_Once()
    {
        audio.PersonSets("speakers", true);
        memory.SetPendingRestore(new MuteToRestore("speakers", false));

        TvOnlyMute.RestoreLeftOver(audio, memory).ShouldBe(LeftOverMute.Restored);

        audio.Muted("speakers").ShouldBeFalse();
        memory.PendingRestore.ShouldBeNull();
        TvOnlyMute.RestoreLeftOver(audio, memory).ShouldBe(LeftOverMute.None);
    }

    [Fact]
    public void ALeftOverMuteForAnOutputThatIsGone_IsForgottenQuietly()
    {
        memory.SetPendingRestore(new MuteToRestore("hdmi", false));

        TvOnlyMute.RestoreLeftOver(audio, memory).ShouldBe(LeftOverMute.OutputGone);

        memory.PendingRestore.ShouldBeNull();
        audio.MuteCalls.ShouldBeEmpty();
    }

    [Fact]
    public void ALeftOverMuteWindowsWillNotUndo_IsTriedAgainNextLaunch()
    {
        audio.RefusesMute = true;
        memory.SetPendingRestore(new MuteToRestore("SPEAKERS", true));

        TvOnlyMute.RestoreLeftOver(audio, memory).ShouldBe(LeftOverMute.NotRestored);

        memory.PendingRestore.ShouldNotBeNull();
        Should.Throw<ArgumentNullException>(() => TvOnlyMute.RestoreLeftOver(null!, memory));
        Should.Throw<ArgumentNullException>(() => TvOnlyMute.RestoreLeftOver(audio, null!));
    }

    /// <summary>Memory that checks the output was not yet muted whenever a mute is written down.</summary>
    private sealed class WatchedMemory(ISoundMemory inner, FakeAudio audio) : ISoundMemory
    {
        public MuteToRestore? PendingRestore => inner.PendingRestore;

        public void SetPendingRestore(MuteToRestore? restore)
        {
            if (restore is not null)
            {
                audio.Muted(restore.DeviceId).ShouldBeFalse("written down before muting, not after");
            }

            inner.SetPendingRestore(restore);
        }

        public bool MuteSilences(string deviceId) => inner.MuteSilences(deviceId);

        public void RememberMuteSilences(string deviceId) => inner.RememberMuteSilences(deviceId);
    }

    [Fact]
    public void AMuteLeftOnRecordForAnotherOutput_DoesNotSayHowThisOneWas()
    {
        memory.SetPendingRestore(new MuteToRestore("usb", true));
        var mute = new TvOnlyMute(audio, memory, clock);

        mute.Begin("speakers").ShouldBeTrue();

        memory.PendingRestore.ShouldBe(new MuteToRestore("speakers", false), "read from Windows, not from the other output's record");
    }
}
