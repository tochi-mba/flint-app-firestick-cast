using Avalonia.Headless.XUnit;
using Flint.App.Tests.Snapshots;
using Flint.App.ViewModels;
using Flint.Core;
using Flint.Core.Settings;
using Flint.Protocol;
using Shouldly;
using static Flint.App.Tests.CastPageFixtures;

namespace Flint.App.Tests;

/// <summary>The Screen page's Sound row: its switch, its words, its meter and the TV's volume.</summary>
public sealed class ScreenPageSoundTests : IDisposable
{
    private static readonly AudioDevice Speakers = new("speakers", "Speakers", true);

    private readonly SettingsService settings = new(new InMemoryAppSettingsStore());
    private readonly FakeAudio audio = new(Speakers);
    private readonly InMemorySoundMemory memory = new();
    private readonly ManualTime clock = new();

    public void Dispose() => settings.Dispose();

    [AvaloniaFact]
    public async Task TheSwitchAndWhereSoundPlays_AreSettings_AndTheWordsFollowTheShare()
    {
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedWithSoundAsync(tv, settings, audio, memory, clock));

        screen.SoundStatus.ShouldBe("This PC's sound plays on the TV with the picture.");
        screen.ChosenDestination.Destination.ShouldBe(SoundDestination.TvOnly);
        screen.ChosenDestination = screen.SoundDestinations[1];
        settings.Current.Screen.SoundDestination.ShouldBe(SoundDestination.TvAndPc);
        screen.ChosenDestination = null!;
        settings.Current.Screen.SoundDestination.ShouldBe(SoundDestination.TvAndPc, "a cleared choice changes nothing");
        screen.ShareSound = false;
        settings.Current.Screen.ShareSound.ShouldBeFalse();
        screen.SoundStatus.ShouldBe("Sound is off.");
        screen.ShareSound = true;

        var sharing = screen.ShareCommand.ExecuteAsync(null);
        await tv.WaitForAsync<VideoConfigMessage>();
        await Until(() => audio.Share is not null && screen.SoundStatus == "Ready. Nothing is playing on this PC yet.");
        screen.ShowSoundMeter.ShouldBeFalse();

        audio.Share!.Stats = new AudioShareStats(5, 0, 0.1f, AudioShareState.Sounding, 0.3f);
        await Until(() => screen.SoundStatus == "Sending sound to the TV.");
        await Until(() => screen.LiveSound == "5 sent, 0 dropped");
        screen.ShowSoundMeter.ShouldBeTrue();
        screen.SoundMeter.ShouldBe(2.0 / 3, tolerance: 1e-6);

        screen.PauseCommand.Execute(null);
        screen.SoundStatus.ShouldBe("Sound is paused with the picture.");
        screen.ShowSoundMeter.ShouldBeFalse();
        screen.ResumeCommand.Execute(null);

        await screen.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
        screen.SoundStatus.ShouldBe("This PC's sound plays on the TV with the picture.");
        screen.LiveSound.ShouldBe(ScreenPageViewModel.NoFigure);
    }

    [AvaloniaFact]
    public async Task AChosenOutputThatHasGone_IsSaid_AndTheDefaultIsOffered()
    {
        settings.Update(current => current with
        {
            Screen = current.Screen with { SoundSource = SoundSource.NamedDevice, SoundDeviceIdentity = "usb-gone" },
        });
        audio.Refusal = new AudioEngineException("The chosen sound output is not connected.", AudioStartFailure.DeviceMissing);
        await using var tv = new LoopbackReceiver();
        using var screen = Page(await PairedWithSoundAsync(tv, settings, audio, memory, clock));
        screen.OfferDefaultOutput.ShouldBeFalse();

        var sharing = screen.ShareCommand.ExecuteAsync(null);
        await tv.WaitForAsync<VideoConfigMessage>();
        await Until(() => screen.OfferDefaultOutput);

        screen.SoundStatus.ShouldBe("Sound is not available. The chosen sound output is not connected.");
        audio.Refusal = null;
        screen.UseDefaultOutputCommand.Execute(null);

        settings.Current.Screen.SoundSource.ShouldBe(SoundSource.DefaultOutput);
        await Until(() => audio.Share is not null && !screen.OfferDefaultOutput);
        audio.Started.Last().DeviceId.ShouldBeNull();
        await screen.Cast.StopMirrorAsync(TestContext.Current.CancellationToken);
        await sharing;
    }

    [AvaloniaFact]
    public async Task TheTvVolume_IsSetOnlyWithATv_AndSaysWhereItIs()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedWithSoundAsync(tv, settings, audio, memory, clock);
        using var screen = Page(cast);

        screen.CanSetTvVolume.ShouldBeTrue();
        screen.TvVolumeText.ShouldBe("TV volume: not set from here yet");
        screen.TvVolume.ShouldBe(100);
        screen.TvVolume = 40.4;
        screen.TvVolumeText.ShouldBe("TV volume: 40%");
        await tv.WaitUntilAsync(received => received.OfType<ControlMessage>().Any(control => control.Event is VolumeControl { Level: 0.4f }));
        screen.TvVolume = 150;
        screen.TvVolume.ShouldBe(100);

        await cast.DisconnectCommand.ExecuteAsync(null);
        screen.CanSetTvVolume.ShouldBeFalse();
        screen.TvVolume = 10;
        screen.TvVolume.ShouldBe(100, "nothing to set without a TV");
    }

    [AvaloniaFact]
    public async Task WhatFlintDidWithThePcsSound_IsPassedOn()
    {
        await using var tv = new LoopbackReceiver();
        var cast = await PairedWithSoundAsync(tv, settings, audio, memory, clock);
        using var screen = Page(cast);
        var told = new List<string?>();
        screen.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(ScreenPageViewModel.SoundNotice))
            {
                told.Add(screen.SoundNotice);
            }
        };

        cast.SoundNotice = "Something happened.";

        screen.SoundNotice.ShouldBe("Something happened.");
        told.ShouldContain("Something happened.");
    }

    private ScreenPageViewModel Page(CastPageViewModel cast) =>
        new(cast, settings, new SnapshotFixtures.FixedDisplays([SnapshotFixtures.MainDisplay]), clock);
}
