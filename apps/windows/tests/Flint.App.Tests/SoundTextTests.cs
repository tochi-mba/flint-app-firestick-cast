using Flint.App.ViewModels;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>What the Screen page says about shared sound, word for word.</summary>
public sealed class SoundTextTests
{
    [Theory]
    [InlineData(false, true, false, AudioShareState.Sounding, null, "Sound is off.")]
    [InlineData(true, false, false, AudioShareState.Off, null, "This PC's sound plays on the TV with the picture.")]
    [InlineData(true, true, false, AudioShareState.Off, null, "Ready. Nothing is playing on this PC yet.")]
    [InlineData(true, true, false, AudioShareState.Ready, null, "Ready. Nothing is playing on this PC yet.")]
    [InlineData(true, true, false, AudioShareState.Sounding, null, "Sending sound to the TV.")]
    [InlineData(true, true, true, AudioShareState.Sounding, null, "Sound is paused with the picture.")]
    [InlineData(true, true, true, AudioShareState.Unavailable, "The chosen sound output was disconnected.", "Sound is not available. The chosen sound output was disconnected.")]
    [InlineData(true, true, false, AudioShareState.Unavailable, null, "Sound is not available. Windows would not share this PC's sound.")]
    public void EachState_HasItsOwnWords(bool shareSound, bool sharing, bool paused, AudioShareState state, string? problem, string expected)
    {
        SoundText.Status(shareSound, sharing, paused, state, problem).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(-1f, 0)]
    [InlineData(0.001f, 0)]
    [InlineData(0.01f, 1.0 / 3)]
    [InlineData(0.1f, 2.0 / 3)]
    [InlineData(1f, 1)]
    [InlineData(4f, 1)]
    public void TheMeter_ReadsInDecibels_FromSixtyBelowFullScale(float level, double expected)
    {
        SoundText.Meter(level).ShouldBe(expected, tolerance: 1e-6);
    }

    [Fact]
    public void ProtectedVideo_IsExplained_AsWindowsNotAFault()
    {
        SoundText.ProtectedContent.ShouldContain("not a fault");
        ScreenPageViewModel.SoundHelp.ShouldBe(SoundText.ProtectedContent);
    }
}
