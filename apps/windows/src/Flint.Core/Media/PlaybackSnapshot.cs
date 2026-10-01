namespace Flint.Core.Media;

/// <summary>What the TV's player is doing, as far as this PC knows.</summary>
public enum PlaybackPhase
{
    /// <summary>Nothing is loaded.</summary>
    Idle = 0,

    /// <summary>The TV is loading or waiting for data.</summary>
    Buffering = 1,

    /// <summary>The file is playing.</summary>
    Playing = 2,

    /// <summary>The file is paused.</summary>
    Paused = 3,

    /// <summary>The file played to its end.</summary>
    Finished = 4,

    /// <summary>The TV could not play the file.</summary>
    Problem = 5,
}

/// <summary>One report from the TV's player, and when it arrived.</summary>
/// <remarks>
/// The TV reports twice a second. Between reports the position is worked out from the last one and
/// the time since, so a progress bar can move smoothly without the TV saying anything more.
/// </remarks>
/// <param name="Phase">What the player was doing.</param>
/// <param name="PositionMs">Where it was, in milliseconds from the start.</param>
/// <param name="DurationMs">How long the file is, in milliseconds; negative when the TV does not know.</param>
/// <param name="Detail">The TV's own words, when it gave any.</param>
/// <param name="ReceivedAt">When the report arrived, on the clock <see cref="PositionAt"/> is given.</param>
public sealed record PlaybackSnapshot(
    PlaybackPhase Phase,
    long PositionMs,
    long DurationMs,
    string Detail,
    DateTimeOffset ReceivedAt)
{
    /// <summary>Whether the TV said how long the file is.</summary>
    public bool HasDuration => DurationMs > 0;

    /// <summary>Where the player is expected to be at <paramref name="now"/>.</summary>
    /// <remarks>
    /// Only a playing file moves. It never runs past the file's length, and a clock that went
    /// backwards gives the reported position, never an earlier or negative one.
    /// </remarks>
    public long PositionAt(DateTimeOffset now)
    {
        var reported = Math.Max(0, PositionMs);
        if (Phase is not PlaybackPhase.Playing || now <= ReceivedAt)
        {
            return reported;
        }

        var moved = reported + (long)(now - ReceivedAt).TotalMilliseconds;
        return HasDuration ? Math.Min(moved, DurationMs) : moved;
    }
}
