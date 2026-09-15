namespace Flint.Core;

/// <summary>Whether a <see cref="CastMode"/> is available on a specific host and device pair.</summary>
public enum ModeStatus
{
    /// <summary>Everything this mode needs is present and proven.</summary>
    Available = 0,

    /// <summary>
    /// Something this mode needs is missing, and the user can probably fix it — ADB debugging is
    /// off, the network is too slow, a driver is absent.
    /// </summary>
    Blocked = 1,

    /// <summary>
    /// This device or host can never do this. Not a diagnostic to act on; a fact to report.
    /// </summary>
    Impossible = 2,

    /// <summary>The capability is unavailable on the current build.</summary>
    NotImplemented = 3,
}
