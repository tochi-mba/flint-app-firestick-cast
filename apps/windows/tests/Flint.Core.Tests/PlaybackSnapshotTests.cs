using Flint.Core.Media;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>Working out where the TV's player is between its reports.</summary>
public sealed class PlaybackSnapshotTests
{
    private static readonly DateTimeOffset Arrived = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Playing_AdvancesWithTheClock()
    {
        var snapshot = new PlaybackSnapshot(PlaybackPhase.Playing, 10_000, 60_000, "", Arrived);

        snapshot.PositionAt(Arrived.AddMilliseconds(250)).ShouldBe(10_250);
        snapshot.PositionAt(Arrived.AddSeconds(3)).ShouldBe(13_000);
    }

    [Fact]
    public void Playing_StopsAtTheDuration()
    {
        var snapshot = new PlaybackSnapshot(PlaybackPhase.Playing, 59_000, 60_000, "", Arrived);

        snapshot.PositionAt(Arrived.AddSeconds(5)).ShouldBe(60_000);
    }

    [Theory]
    [InlineData(PlaybackPhase.Paused)]
    [InlineData(PlaybackPhase.Buffering)]
    [InlineData(PlaybackPhase.Finished)]
    [InlineData(PlaybackPhase.Problem)]
    [InlineData(PlaybackPhase.Idle)]
    public void AnythingButPlaying_DoesNotMove(PlaybackPhase phase)
    {
        var snapshot = new PlaybackSnapshot(phase, 10_000, 60_000, "", Arrived);

        snapshot.PositionAt(Arrived.AddSeconds(5)).ShouldBe(10_000);
    }

    [Fact]
    public void AnUnknownLength_AdvancesWithoutACeiling()
    {
        var snapshot = new PlaybackSnapshot(PlaybackPhase.Playing, 10_000, -1, "", Arrived);

        snapshot.HasDuration.ShouldBeFalse();
        snapshot.PositionAt(Arrived.AddHours(2)).ShouldBe(10_000 + 7_200_000);
    }

    [Fact]
    public void AClockThatWentBackwards_GivesTheReportedPosition()
    {
        var snapshot = new PlaybackSnapshot(PlaybackPhase.Playing, 10_000, 60_000, "", Arrived);

        snapshot.PositionAt(Arrived.AddSeconds(-30)).ShouldBe(10_000);
    }

    [Fact]
    public void ANegativeReport_IsNeverGivenBack()
    {
        var snapshot = new PlaybackSnapshot(PlaybackPhase.Paused, -500, 60_000, "", Arrived);

        snapshot.PositionAt(Arrived.AddSeconds(-1)).ShouldBe(0);
    }

    [Theory]
    [InlineData(60_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void HasDuration_OnlyForAPositiveLength(long durationMs, bool expected) =>
        new PlaybackSnapshot(PlaybackPhase.Playing, 0, durationMs, "", Arrived).HasDuration.ShouldBe(expected);
}
