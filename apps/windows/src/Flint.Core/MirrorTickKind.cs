namespace Flint.Core;

/// <summary>
/// What one mirror tick produced.
/// </summary>
/// <remarks>
/// Values must match <c>mirror_tick</c> in the engine's <c>ffi.rs</c>. Changing one silently
/// changes its meaning on the other side of the boundary.
/// </remarks>
public enum MirrorTickKind
{
    /// <summary>
    /// Nothing to send.
    /// </summary>
    /// <remarks>
    /// Neither an error nor rare: a still desktop produces these continuously, because the engine
    /// deliberately does not re-encode a picture the receiver is already showing.
    /// </remarks>
    Nothing = 0,

    /// <summary>An access unit is ready for the wire.</summary>
    Encoded = 1,

    /// <summary>
    /// Capture was lost and re-created.
    /// </summary>
    /// <remarks>
    /// A lock screen, a UAC prompt, or a resolution change all arrive here. The next access unit
    /// will be a key frame, because everything the receiver held as a reference is now stale.
    /// </remarks>
    Recovered = 2,
}
