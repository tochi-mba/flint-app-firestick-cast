using Avalonia.Input;
using Flint.App.Controls;
using Flint.App.ViewModels;
using Flint.Core.Media;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The Now Playing card: what it shows from the TV's reports, and what each control sends.
/// </summary>
/// <remarks>
/// Driven by a clock that moves only when the test moves it, so every hold, tick and gathered skip
/// is exact rather than raced.
/// </remarks>
public sealed class NowPlayingViewModelTests
{
    private readonly ManualTime clock = new();
    private readonly RecordingRemote remote = new();

    [Fact]
    public void Empty_IsHidden_AndOffersNothing()
    {
        using var card = Card();

        card.IsActive.ShouldBeFalse();
        card.PlayPauseCommand.CanExecute(null).ShouldBeFalse();
        card.StopCommand.CanExecute(null).ShouldBeFalse();
        card.HandleKey(Key.Space).ShouldBeFalse();
        remote.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void Sending_ShowsProgress_AndOnlyCancel()
    {
        using var card = Card();
        card.BeginSending("holiday.mp4", isPicture: false);

        card.ReportSendProgress(0.42);

        card.IsActive.ShouldBeTrue();
        card.IsSending.ShouldBeTrue();
        card.Title.ShouldBe("holiday.mp4");
        card.PillText.ShouldBe("SENDING 42%");
        card.PillTone.ShouldBe(Tone.Neutral);
        card.CancelCommand.CanExecute(null).ShouldBeTrue();
        card.PlayPauseCommand.CanExecute(null).ShouldBeFalse();
        card.StopCommand.CanExecute(null).ShouldBeFalse();
        card.ShowsTransport.ShouldBeFalse();
        card.ShowsSeekBar.ShouldBeFalse();
    }

    [Fact]
    public void ProgressAfterTheSendEnded_ChangesNothing()
    {
        using var card = Playing(position: 0);

        card.ReportSendProgress(0.5);

        card.SendPercent.ShouldBe(0);
        card.PillText.ShouldBe("PLAYING");
    }

    [Fact]
    public void Cancel_AsksTheRemoteToAbandonTheSend()
    {
        using var card = Card();
        card.BeginSending("holiday.mp4", isPicture: false);

        card.CancelCommand.Execute(null);

        remote.Sent.ShouldBe(["cancel"]);
    }

    [Theory]
    [InlineData(PlaybackPhase.Buffering, "BUFFERING", Tone.Neutral, "PAUSE")]
    [InlineData(PlaybackPhase.Playing, "PLAYING", Tone.Signal, "PAUSE")]
    [InlineData(PlaybackPhase.Paused, "PAUSED", Tone.Neutral, "PLAY")]
    [InlineData(PlaybackPhase.Finished, "FINISHED", Tone.Neutral, "REPLAY")]
    [InlineData(PlaybackPhase.Problem, "PROBLEM", Tone.Live, "PLAY")]
    public void AReport_SetsStatePositionDurationAndPill(PlaybackPhase phase, string pill, Tone tone, string button)
    {
        using var card = Playing(position: 0);

        card.Apply(Report(phase, 61_000, 5_520_000, phase is PlaybackPhase.Problem ? "The file is damaged." : ""));

        card.Phase.ShouldBe(phase);
        card.PillText.ShouldBe(pill);
        card.PillTone.ShouldBe(tone);
        card.PlayPauseLabel.ShouldBe(button);
        card.DurationMs.ShouldBe(5_520_000);
        card.PositionMs.ShouldBe(61_000);
        card.ElapsedText.ShouldBe("1:01");
        card.RemainingText.ShouldBe("-1:30:59");
    }

    [Fact]
    public void AProblemReport_ShowsTheTvsReason_AndTryAgain()
    {
        using var card = Playing(position: 0);

        card.Apply(Report(PlaybackPhase.Problem, 0, -1, "The file is damaged."));

        card.ShowsProblem.ShouldBeTrue();
        card.ProblemText.ShouldBe("The file is damaged.");
        card.ShowsTransport.ShouldBeFalse();
        card.ShowsSeekBar.ShouldBeFalse();
        card.ShowsElapsedOnly.ShouldBeFalse();
        card.TryAgainCommand.CanExecute(null).ShouldBeTrue();
        card.TryAgainCommand.Execute(null);
        remote.Sent.ShouldBe(["try again"]);
    }

    [Fact]
    public void ShowProblem_SaysWhy_WithoutAReport()
    {
        using var card = Card();
        card.BeginSending("holiday.mp4", isPicture: false);

        card.ShowProblem("The TV did not confirm playback.");

        card.IsSending.ShouldBeFalse();
        card.ShowsProblem.ShouldBeTrue();
        card.ProblemText.ShouldBe("The TV did not confirm playback.");
    }

    [Fact]
    public void ShowProblem_OnAnEmptyCard_ChangesNothing()
    {
        using var card = Card();

        card.ShowProblem("anything");

        card.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void AnIdleReport_MeansTheTvCleared_AndHidesTheCard()
    {
        using var card = Playing(position: 10_000);

        card.Apply(Report(PlaybackPhase.Idle, 0, -1));

        card.IsActive.ShouldBeFalse();
        card.Title.ShouldBeEmpty();
        clock.ActiveTimers.ShouldBe(0);
    }

    [Fact]
    public void Between_Reports_TheBarMovesOnItsOwn_AndStopsAtTheEnd()
    {
        using var card = Playing(position: 58_000, duration: 60_000);
        var changes = 0;
        card.PropertyChanged += (_, changed) => changes += changed.PropertyName == nameof(card.PositionMs) ? 1 : 0;

        clock.Advance(TimeSpan.FromMilliseconds(1_000));
        card.PositionMs.ShouldBe(59_000);
        changes.ShouldBe(4, "four quarter-second ticks");

        clock.Advance(TimeSpan.FromSeconds(5));
        card.PositionMs.ShouldBe(60_000);
    }

    [Fact]
    public void WhilePaused_TheBarStaysPut()
    {
        using var card = Playing(position: 10_000);
        card.Apply(Report(PlaybackPhase.Paused, 10_000, 60_000));

        clock.Advance(TimeSpan.FromSeconds(3));

        card.PositionMs.ShouldBe(10_000);
    }

    [Fact]
    public void ReportsDuringADrag_DoNotMoveTheBar()
    {
        using var card = Playing(position: 10_000);

        card.BeginScrub(card.SeekSeconds);
        card.SeekSeconds = 40;
        card.Apply(Report(PlaybackPhase.Playing, 12_000, 60_000));
        clock.Advance(TimeSpan.FromSeconds(1));

        card.IsScrubbing.ShouldBeTrue();
        card.PositionMs.ShouldBe(40_000);
        card.ScrubText.ShouldBe("0:40");
        remote.Sent.ShouldBeEmpty("nothing is sent until the bar is let go");
    }

    [Fact]
    public void LettingGo_SendsExactlyOneSeek_AndHoldsTheTarget_UntilTheTvConfirmsIt()
    {
        using var card = Playing(position: 10_000);
        card.BeginScrub(card.SeekSeconds);
        card.SeekSeconds = 30;
        card.SeekSeconds = 40;

        card.CommitSeek(40);

        remote.Sent.ShouldBe(["seek 40000"]);
        card.IsScrubbing.ShouldBeFalse();

        // A late report from before the seek does not pull the bar back.
        card.Apply(Report(PlaybackPhase.Playing, 11_000, 60_000));
        card.PositionMs.ShouldBe(40_000);

        // A report within two seconds of the target confirms it; the bar follows the TV again.
        card.Apply(Report(PlaybackPhase.Playing, 41_500, 60_000));
        clock.Advance(TimeSpan.FromMilliseconds(500));
        card.PositionMs.ShouldBe(42_000);
    }

    [Fact]
    public void AHeldSeekTheTvNeverConfirms_IsLetGoAfterTwoSeconds()
    {
        using var card = Playing(position: 10_000);
        card.BeginScrub(card.SeekSeconds);

        card.CommitSeek(40);
        card.Apply(Report(PlaybackPhase.Paused, 11_000, 60_000));
        clock.Advance(TimeSpan.FromMilliseconds(1_750));
        card.PositionMs.ShouldBe(40_000, "still within the hold");

        clock.Advance(TimeSpan.FromMilliseconds(250));
        card.PositionMs.ShouldBe(11_000, "what the TV says");
    }

    [Fact]
    public void ASeek_IsKeptWithinTheFile()
    {
        using var card = Playing(position: 10_000, duration: 60_000);
        card.BeginScrub(card.SeekSeconds);
        card.SeekSeconds = 999;
        card.PositionMs.ShouldBe(60_000);

        card.CommitSeek(-5);

        remote.Sent.ShouldBe(["seek 0"]);
    }

    [Fact]
    public void WritingTheBar_WhenNotDragging_ChangesNothing()
    {
        using var card = Playing(position: 10_000);

        card.SeekSeconds = 50;

        card.PositionMs.ShouldBe(10_000);
        remote.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void PlayAndPause_SendTheRightAction_AndFlipAtOnce()
    {
        using var card = Playing(position: 10_000);

        card.PlayPauseCommand.Execute(null);
        card.Phase.ShouldBe(PlaybackPhase.Paused);
        card.PlayPauseLabel.ShouldBe("PLAY");
        card.PlayPauseName.ShouldBe("Play");

        card.Apply(Report(PlaybackPhase.Paused, 10_000, 60_000));
        card.PlayPauseCommand.Execute(null);
        card.Phase.ShouldBe(PlaybackPhase.Playing);
        card.PlayPauseName.ShouldBe("Pause");

        remote.Sent.ShouldBe(["pause", "play"]);
    }

    [Fact]
    public void AToggleTheTvDoesNotConfirm_GoesBackToWhatTheTvSays_AfterASecondAndAHalf()
    {
        using var card = Playing(position: 10_000);

        card.PlayPauseCommand.Execute(null);
        card.Apply(Report(PlaybackPhase.Playing, 10_500, 60_000));
        clock.Advance(TimeSpan.FromMilliseconds(1_250));
        card.Phase.ShouldBe(PlaybackPhase.Paused, "still waiting for the TV");

        clock.Advance(TimeSpan.FromMilliseconds(250));
        card.Phase.ShouldBe(PlaybackPhase.Playing);
        card.PlayPauseLabel.ShouldBe("PAUSE");
    }

    [Fact]
    public void AToggleTheTvConfirms_Stays()
    {
        using var card = Playing(position: 10_000);

        card.PlayPauseCommand.Execute(null);
        card.Apply(Report(PlaybackPhase.Paused, 10_000, 60_000));
        clock.Advance(TimeSpan.FromSeconds(3));

        card.Phase.ShouldBe(PlaybackPhase.Paused);
    }

    [Fact]
    public void PausingWhileBuffering_SendsPause()
    {
        using var card = Playing(position: 10_000);
        card.Apply(Report(PlaybackPhase.Buffering, 10_000, 60_000));

        card.PlayPauseCommand.Execute(null);

        remote.Sent.ShouldBe(["pause"]);
    }

    [Fact]
    public void Replay_AfterTheEnd_SeeksToTheStartThenPlays()
    {
        using var card = Playing(position: 0);
        card.Apply(Report(PlaybackPhase.Finished, 60_000, 60_000));
        card.PlayPauseName.ShouldBe("Play again from the start");

        card.PlayPauseCommand.Execute(null);

        remote.Sent.ShouldBe(["seek 0", "play"]);
        card.PositionMs.ShouldBe(0);
        card.Phase.ShouldBe(PlaybackPhase.Playing);
    }

    [Fact]
    public void SkipBack_FromFourSeconds_GoesToTheStart()
    {
        using var card = Playing(position: 4_000);

        card.SkipBackCommand.Execute(null);
        clock.Advance(NowPlayingViewModel.SkipGather);

        remote.Sent.ShouldBe(["seek 0"]);
    }

    [Fact]
    public void SkipForward_NearTheEnd_StopsAtTheEnd()
    {
        using var card = Playing(position: 50_000, duration: 60_000);

        card.SkipForwardCommand.Execute(null);
        clock.Advance(NowPlayingViewModel.SkipGather);

        remote.Sent.ShouldBe(["seek 60000"]);
    }

    [Fact]
    public void ThreeQuickSkips_SendOneSeek_ForTheCombinedDistance()
    {
        using var card = Playing(position: 100_000, duration: 600_000);

        card.SkipForwardCommand.Execute(null);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        card.SkipForwardCommand.Execute(null);
        clock.Advance(TimeSpan.FromMilliseconds(200));
        card.SkipForwardCommand.Execute(null);
        card.PositionMs.ShouldBe(190_000, "the bar shows where it is going at once");
        remote.Sent.ShouldBeEmpty();

        clock.Advance(NowPlayingViewModel.SkipGather);

        remote.Sent.ShouldBe(["seek 190000"]);
    }

    [Fact]
    public void SkipDistances_ComeFromSettings_AndAChangeAppliesToTheNextPress()
    {
        var settings = new SettingsService(new InMemoryAppSettingsStore());
        using var card = Playing(position: 100_000, duration: 600_000, settings);
        card.SkipBackLabel.ShouldBe("−10 S");
        card.SkipForwardName.ShouldBe("Forward 30 seconds");

        settings.Update(current => current with { Media = current.Media with { SkipBackSeconds = 5, SkipForwardSeconds = 60 } });
        card.SkipBackLabel.ShouldBe("−5 S");
        card.SkipBackName.ShouldBe("Back 5 seconds");
        card.SkipForwardLabel.ShouldBe("+60 S");
        card.SkipBackCommand.Execute(null);
        clock.Advance(NowPlayingViewModel.SkipGather);

        remote.Sent.ShouldBe(["seek 95000"]);
    }

    [Fact]
    public void AFileOfUnknownLength_ShowsElapsedOnly_AndCannotSkipOrSeek()
    {
        using var card = Playing(position: 10_000, duration: -1);

        card.ShowsSeekBar.ShouldBeFalse();
        card.ShowsElapsedOnly.ShouldBeTrue();
        card.SkipBackCommand.CanExecute(null).ShouldBeFalse();
        card.SkipForwardCommand.CanExecute(null).ShouldBeFalse();
        card.SkipTip.ShouldBe("This file does not report its length.");
        card.PlayPauseCommand.CanExecute(null).ShouldBeTrue();

        card.BeginScrub(card.SeekSeconds);
        card.IsScrubbing.ShouldBeFalse();
        card.CommitSeek(5);
        remote.Sent.ShouldBeEmpty();
    }

    [Fact]
    public void AFileOfKnownLength_HasNoSkipTip()
    {
        using var card = Playing(position: 10_000);

        card.SkipTip.ShouldBeNull();
        card.ShowsSeekBar.ShouldBeTrue();
    }

    [Fact]
    public void APicture_HasNoBarAndNoPlayButton()
    {
        using var card = Card();
        card.BeginSending("beach.jpg", isPicture: true);
        card.SendFinished(Report(PlaybackPhase.Playing, 0, -1));

        card.ShowsPictureNote.ShouldBeTrue();
        card.PillText.ShouldBe("SHOWING");
        card.PillTone.ShouldBe(Tone.Signal);
        card.ShowsSeekBar.ShouldBeFalse();
        card.ShowsElapsedOnly.ShouldBeFalse();
        card.ShowsTransport.ShouldBeFalse();
        card.PlayPauseCommand.CanExecute(null).ShouldBeFalse();
        card.StopCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void Volume_IsUnset_UntilItIsFirstSetFromHere()
    {
        using var card = Playing(position: 0);

        card.HasVolume.ShouldBeFalse();
        card.VolumeText.ShouldBe("TV volume: not set from here yet");
        card.VolumePercent.ShouldBe(0);
        card.MuteCommand.CanExecute(null).ShouldBeFalse();
        card.VolumeUpCommand.CanExecute(null).ShouldBeFalse();
        card.HandleKey(Key.Up).ShouldBeFalse();
        remote.Sent.ShouldBeEmpty();

        card.VolumePercent = 40;

        card.HasVolume.ShouldBeTrue();
        card.VolumeText.ShouldBe("TV volume: 40%");
        remote.Sent.ShouldBe(["volume 0.4"]);
    }

    [Fact]
    public void Mute_SendsZero_AndUnmute_RestoresTheLevel()
    {
        using var card = Playing(position: 0);
        card.VolumePercent = 40;

        card.MuteCommand.Execute(null);
        card.IsMuted.ShouldBeTrue();
        card.MuteLabel.ShouldBe("UNMUTE");
        card.VolumeText.ShouldBe("TV volume: 0%");

        card.MuteCommand.Execute(null);
        card.IsMuted.ShouldBeFalse();
        card.MuteLabel.ShouldBe("MUTE");

        remote.Sent.ShouldBe(["volume 0.4", "volume 0", "volume 0.4"]);
    }

    [Fact]
    public void TheVolumeStep_IsHonoured_AndAChangeAppliesToTheNextPress()
    {
        var settings = new SettingsService(new InMemoryAppSettingsStore());
        using var card = Playing(position: 0, settings: settings);
        card.VolumePercent = 50;

        card.VolumeUpCommand.Execute(null);
        settings.Update(current => current with { Media = current.Media with { VolumeStepPercent = 10 } });
        card.VolumeDownCommand.Execute(null);

        remote.Sent.ShouldBe(["volume 0.5", "volume 0.55", "volume 0.45"]);
    }

    [Fact]
    public void Volume_StaysWithinTheRange()
    {
        using var card = Playing(position: 0);
        card.VolumePercent = 98;

        card.VolumeUpCommand.Execute(null);
        card.VolumePercent = -20;

        remote.Sent.ShouldBe(["volume 0.98", "volume 1", "volume 0"]);
    }

    [Fact]
    public void TurningUpWhileMuted_StartsFromTheLevelBeforeMuting()
    {
        using var card = Playing(position: 0);
        card.VolumePercent = 40;
        card.MuteCommand.Execute(null);

        card.VolumeUpCommand.Execute(null);

        card.IsMuted.ShouldBeFalse();
        remote.Sent.Last().ShouldBe("volume 0.45");
    }

    [Fact]
    public void Stop_AsksTheRemoteToStop()
    {
        using var card = Playing(position: 0);

        card.StopCommand.Execute(null);

        remote.Sent.ShouldBe(["stop"]);
    }

    [Fact]
    public void LosingTheConnection_DisablesEveryControl_AndKeepsWhatWasPlayingAndWhere()
    {
        using var card = Playing(position: 30_000);
        card.VolumePercent = 40;
        clock.Advance(TimeSpan.FromSeconds(2));

        card.ConnectionLost("The connection to Living Room was lost.");
        clock.Advance(TimeSpan.FromSeconds(10));

        card.IsActive.ShouldBeTrue();
        card.IsConnectionLost.ShouldBeTrue();
        card.ConnectionLostText.ShouldBe("The connection to Living Room was lost.");
        card.PillText.ShouldBe("DISCONNECTED", "it no longer knows what the TV is doing");
        card.PillTone.ShouldBe(Tone.Neutral);
        card.Title.ShouldBe("holiday.mp4");
        card.PositionMs.ShouldBe(32_000);
        card.CanControl.ShouldBeFalse();
        foreach (var command in new[]
        {
            card.PlayPauseCommand, card.SkipBackCommand, card.SkipForwardCommand, card.StopCommand,
            card.CancelCommand, card.TryAgainCommand, card.MuteCommand, card.VolumeUpCommand,
            card.VolumeDownCommand,
        })
        {
            command.CanExecute(null).ShouldBeFalse();
        }

        card.VolumePercent = 80;
        card.Apply(Report(PlaybackPhase.Playing, 50_000, 60_000));
        card.PositionMs.ShouldBe(32_000);
        remote.Sent.ShouldBe(["volume 0.4"]);
        clock.ActiveTimers.ShouldBe(0);
    }

    [Fact]
    public void LosingTheConnection_WithNothingShowing_ChangesNothing()
    {
        using var card = Card();

        card.ConnectionLost("lost");

        card.IsActive.ShouldBeFalse();
        card.IsConnectionLost.ShouldBeFalse();
    }

    [Fact]
    public void LosingTheConnection_MidSkip_DropsTheSkip()
    {
        using var card = Playing(position: 10_000);
        card.SkipForwardCommand.Execute(null);

        card.ConnectionLost("lost");
        clock.Advance(TimeSpan.FromSeconds(1));

        remote.Sent.ShouldBeEmpty();
        card.PositionMs.ShouldBe(40_000, "frozen where the person last saw it");
    }

    [Fact]
    public void ALateReportForThePreviousFile_IsIgnored_WhileTheNextIsSending()
    {
        using var card = Playing(position: 10_000);

        card.BeginSending("second.mp4", isPicture: false);
        card.Apply(Report(PlaybackPhase.Finished, 60_000, 60_000));

        // The TV clears the first file as the second loads; that must not hide the card mid-send.
        card.Apply(Report(PlaybackPhase.Idle, 0, -1));

        card.IsActive.ShouldBeTrue();
        card.IsSending.ShouldBeTrue();
        card.PillText.ShouldBe("SENDING 0%");
        card.Title.ShouldBe("second.mp4");
    }

    [Fact]
    public void SendFinished_WhenNothingWasSending_ChangesNothing()
    {
        using var card = Card();

        card.SendFinished(Report(PlaybackPhase.Playing, 0, 60_000));

        card.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void TurningDownWhileMuted_StartsFromTheLevelBeforeMuting()
    {
        using var card = Playing(position: 0);
        card.VolumePercent = 40;
        card.MuteCommand.Execute(null);

        card.VolumeDownCommand.Execute(null);

        remote.Sent.Last().ShouldBe("volume 0.35");
    }

    [Fact]
    public void ASkipPostedBeforeTheCardWasCleared_SendsNothing()
    {
        var posted = new List<SendOrPostCallback>();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CollectingContext(posted));
        NowPlayingViewModel card;
        try
        {
            card = new NowPlayingViewModel(remote, clock);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (card)
        {
            card.BeginSending("holiday.mp4", isPicture: false);
            card.SendFinished(Report(PlaybackPhase.Playing, 10_000, 60_000));
            card.SkipForwardCommand.Execute(null);
            clock.Advance(NowPlayingViewModel.SkipGather);
            card.Clear();

            foreach (var callback in posted.ToList())
            {
                callback(null);
            }

            remote.Sent.ShouldBeEmpty();
        }
    }

    [Fact]
    public void WithNoClockGiven_TheCardUsesTheSystemOne()
    {
        using var card = new NowPlayingViewModel(remote);

        card.BeginSending("holiday.mp4", isPicture: false);
        card.SendFinished(new PlaybackSnapshot(PlaybackPhase.Paused, 1_000, 60_000, "", DateTimeOffset.UtcNow));

        card.PositionMs.ShouldBe(1_000);
    }

    [Fact]
    public void TheRightHandTime_SwitchesToTheWholeLength_AndThatIsRemembered()
    {
        var settings = new SettingsService(new InMemoryAppSettingsStore());
        using var card = Playing(position: 61_000, duration: 5_520_000, settings);
        card.RemainingText.ShouldBe("-1:30:59");

        card.ToggleTimeDisplayCommand.Execute(null);

        card.ShowsTotalTime.ShouldBeTrue();
        card.RemainingText.ShouldBe("1:32:00");
        settings.Current.Media.ShowTotalTime.ShouldBeTrue();

        card.ToggleTimeDisplayCommand.Execute(null);
        card.RemainingText.ShouldBe("-1:30:59");
    }

    [Fact]
    public void TheTimeDisplay_Toggles_EvenWithoutLiveSettings()
    {
        using var card = Playing(position: 0, duration: 60_000);

        card.ToggleTimeDisplayCommand.Execute(null);

        card.RemainingText.ShouldBe("1:00");
    }

    [Fact]
    public void TheBar_IsSpokenInWords()
    {
        using var card = Playing(position: 760_000, duration: 5_520_000);

        card.SpokenPosition.ShouldBe("12 minutes 40 seconds of 1 hour 32 minutes");
    }

    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(1_000, "1 second")]
    [InlineData(61_000, "1 minute 1 second")]
    [InlineData(7_322_000, "2 hours 2 minutes 2 seconds")]
    [InlineData(3_600_000, "1 hour")]
    [InlineData(-5, "0 seconds")]
    public void Spoken_Times(long milliseconds, string expected) =>
        NowPlayingViewModel.Spoken(milliseconds).ShouldBe(expected);

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59_999, "0:59")]
    [InlineData(760_000, "12:40")]
    [InlineData(5_520_000, "1:32:00")]
    [InlineData(-1, "0:00")]
    public void Written_Times(long milliseconds, string expected) =>
        NowPlayingViewModel.FormatTime(milliseconds).ShouldBe(expected);

    [Theory]
    [InlineData(Key.Space, "pause")]
    [InlineData(Key.S, "stop")]
    [InlineData(Key.M, "volume 0")]
    [InlineData(Key.Up, "volume 0.45")]
    [InlineData(Key.Down, "volume 0.35")]
    public void Keys_DoWhatTheirButtonsDo(Key key, string sent)
    {
        using var card = Playing(position: 100_000, duration: 600_000);
        card.VolumePercent = 40;
        remote.Sent.Clear();

        card.HandleKey(key).ShouldBeTrue();

        remote.Sent.ShouldBe([sent]);
    }

    [Theory]
    [InlineData(Key.Left, "seek 90000")]
    [InlineData(Key.Right, "seek 130000")]
    public void ArrowKeys_Skip(Key key, string sent)
    {
        using var card = Playing(position: 100_000, duration: 600_000);

        card.HandleKey(key).ShouldBeTrue();
        clock.Advance(NowPlayingViewModel.SkipGather);

        remote.Sent.ShouldBe([sent]);
    }

    [Fact]
    public void OtherKeys_AreLeftAlone()
    {
        using var card = Playing(position: 0);

        card.HandleKey(Key.Q).ShouldBeFalse();
    }

    [Fact]
    public void AControlTheTvNeverReceives_IsDroppedQuietly()
    {
        using var card = Playing(position: 0);
        remote.Failure = new IOException("The receiver session is no longer connected.");

        Should.NotThrow(() => card.PlayPauseCommand.Execute(null));
        Should.NotThrow(() => card.StopCommand.Execute(null));
    }

    [Fact]
    public void Clear_HidesTheCard_AndStopsItsClock()
    {
        using var card = Playing(position: 0);

        card.Clear();

        card.IsActive.ShouldBeFalse();
        clock.ActiveTimers.ShouldBe(0);
    }

    [Fact]
    public void Dispose_StopsItsClock()
    {
        var card = Playing(position: 0);
        card.SkipForwardCommand.Execute(null);

        card.Dispose();

        clock.ActiveTimers.ShouldBe(0);
    }

    [Fact]
    public void TimersPostToTheContextTheCardWasMadeOn()
    {
        var posted = new List<SendOrPostCallback>();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new CollectingContext(posted));
        NowPlayingViewModel card;
        try
        {
            card = new NowPlayingViewModel(remote, clock);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (card)
        {
            card.BeginSending("holiday.mp4", isPicture: false);
            card.SendFinished(Report(PlaybackPhase.Playing, 0, 60_000));
            clock.Advance(TimeSpan.FromSeconds(1));
            card.Apply(Report(PlaybackPhase.Paused, 0, 60_000));

            posted.Count.ShouldBe(4);
            posted[0](null);
            card.PositionMs.ShouldBe(0, "the tick ran on the card's own context, after the report");
        }
    }

    [Fact]
    public void ARemoteIsRequired() =>
        Should.Throw<ArgumentNullException>(() => new NowPlayingViewModel(null!));

    [Fact]
    public void SettingsAreRequired()
    {
        using var card = Card();

        Should.Throw<ArgumentNullException>(() => card.UseSettings(null!));
    }

    [Fact]
    public void AMissingReport_IsRefused()
    {
        using var card = Card();

        Should.Throw<ArgumentNullException>(() => card.Apply(null!));
        Should.Throw<ArgumentNullException>(() => card.SendFinished(null!));
    }

    private NowPlayingViewModel Card(ISettingsService? settings = null)
    {
        var card = new NowPlayingViewModel(remote, clock);
        if (settings is not null)
        {
            card.UseSettings(settings);
        }

        return card;
    }

    private NowPlayingViewModel Playing(long position, long duration = 60_000, ISettingsService? settings = null)
    {
        var card = Card(settings);
        card.BeginSending("holiday.mp4", isPicture: false);
        card.SendFinished(Report(PlaybackPhase.Playing, position, duration));
        return card;
    }

    private PlaybackSnapshot Report(PlaybackPhase phase, long position, long duration, string detail = "") =>
        new(phase, position, duration, detail, clock.GetUtcNow());

    /// <summary>Records what the card asked the TV to do, in order.</summary>
    private sealed class RecordingRemote : IMediaRemote
    {
        public List<string> Sent { get; } = [];

        public Exception? Failure { get; set; }

        public Task SendTransportAsync(TransportAction action, long positionMs = -1) => Record(action switch
        {
            TransportAction.SeekTo => $"seek {positionMs}",
            _ => action.ToString().ToLowerInvariant(),
        });

        public Task SetVolumeAsync(float level) =>
            Record($"volume {level.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        public Task StopAsync() => Record("stop");

        public Task CancelSendAsync() => Record("cancel");

        public Task TryAgainAsync() => Record("try again");

        private Task Record(string what)
        {
            Sent.Add(what);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class CollectingContext(List<SendOrPostCallback> posted) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => posted.Add(d);
    }
}
