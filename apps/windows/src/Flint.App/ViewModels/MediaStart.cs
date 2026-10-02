namespace Flint.App.ViewModels;

/// <summary>Whether a file may take the TV from whatever is on it.</summary>
public enum MediaTakeover
{
    /// <summary>The person asked for it: ask before replacing anything, as every claim does.</summary>
    Ask = 0,

    /// <summary>The queue moved on by itself: only a TV showing nothing, or a file, may be used.</summary>
    OnlyIfFree = 1,
}

/// <summary>What became of a file sent to the TV.</summary>
public enum MediaStart
{
    /// <summary>Not sent: there is no connection to the TV.</summary>
    NotSent = 0,

    /// <summary>The TV is playing it.</summary>
    Playing = 1,

    /// <summary>Sent, and the TV could not play it.</summary>
    Refused = 2,

    /// <summary>The person stopped the send.</summary>
    Cancelled = 3,

    /// <summary>The person kept what the TV was showing.</summary>
    Kept = 4,

    /// <summary>The TV is showing something else, which only the person may replace.</summary>
    Waiting = 5,
}
