using Shouldly;

namespace Flint.Core.Tests;

/// <summary>The engine shells get when none is handed over: no sound, and no touching this PC's.</summary>
public sealed class UnavailableAudioEngineTests
{
    [Fact]
    public void ItHasNoOutputs_StartsNothing_AndNeverTouchesAMute()
    {
        var engine = new UnavailableAudioEngine();

        engine.ListDevices().ShouldBeEmpty();
        Should.Throw<AudioEngineException>(() => engine.Start(new AudioShareOptions())).Reason.ShouldBe(AudioStartFailure.EngineMissing);
        engine.IsMuted(null).ShouldBeNull();
        engine.SetMuted("speakers", true).ShouldBeFalse();
    }
}
