namespace Flint.Core;

/// <summary>A mirror session's clock: how long it has been capturing.</summary>
/// <remarks>
/// A sound share started during a mirror times its packets from this point, so the TV lines picture
/// and sound up on one clock. Kept apart from <see cref="IMirrorEngineSession"/> so a session
/// without a clock of its own need not invent one.
/// </remarks>
public interface IMirrorClock
{
    /// <summary>Microseconds since capture started, on the clock frames are timed by.</summary>
    long ReadElapsedUs();
}
