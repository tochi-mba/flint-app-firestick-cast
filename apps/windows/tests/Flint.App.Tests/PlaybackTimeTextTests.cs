using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>Times as the Now Playing card writes them on the bar and says them to a screen reader.</summary>
public sealed class PlaybackTimeTextTests
{
    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(1_000, "1 second")]
    [InlineData(61_000, "1 minute 1 second")]
    [InlineData(7_322_000, "2 hours 2 minutes 2 seconds")]
    [InlineData(3_600_000, "1 hour")]
    [InlineData(-5, "0 seconds")]
    public void Spoken_Times(long milliseconds, string expected) =>
        PlaybackTimeText.Spoken(milliseconds).ShouldBe(expected);

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(59_999, "0:59")]
    [InlineData(760_000, "12:40")]
    [InlineData(5_520_000, "1:32:00")]
    [InlineData(-1, "0:00")]
    public void Written_Times(long milliseconds, string expected) =>
        PlaybackTimeText.Format(milliseconds).ShouldBe(expected);
}
