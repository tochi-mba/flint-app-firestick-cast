namespace Flint.Protocol;

/// <summary>Protocol version bounds this build understands.</summary>
public static class ProtocolVersion
{
    /// <summary>The oldest payload version this build can decode.</summary>
    public const int MinSupported = 1;

    /// <summary>The version this build encodes.</summary>
    public const int Current = 4;

    /// <summary>
    /// Chooses the payload version two peers will speak.
    /// </summary>
    /// <returns>
    /// The highest version both ranges cover, or <see langword="null"/> when they do not overlap.
    /// A session with no overlap closes rather than attempting a downgrade to a version neither
    /// side implements.
    /// </returns>
    public static int? Negotiate(int localMinimum, int localMaximum, int remoteMinimum, int remoteMaximum)
    {
        if (localMinimum > localMaximum || remoteMinimum > remoteMaximum)
        {
            return null;
        }

        var lower = Math.Max(localMinimum, remoteMinimum);
        var upper = Math.Min(localMaximum, remoteMaximum);
        return lower <= upper ? upper : null;
    }
}
