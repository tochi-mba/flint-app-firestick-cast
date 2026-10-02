using Avalonia.Input;
using Flint.App.ViewModels;
using Flint.Core.Settings;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>The Now Playing card's volume controls, and the Media page's keys.</summary>
public sealed partial class NowPlayingViewModelTests
{
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
    public void TurningDownWhileMuted_StartsFromTheLevelBeforeMuting()
    {
        using var card = Playing(position: 0);
        card.VolumePercent = 40;
        card.MuteCommand.Execute(null);

        card.VolumeDownCommand.Execute(null);

        remote.Sent.Last().ShouldBe("volume 0.35");
    }

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
}
