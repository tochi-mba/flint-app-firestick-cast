namespace Flint.Core;

/// <summary>Whether a share is sending, and what the TV shows while it is not.</summary>
/// <remarks>Values match <c>pause_mode</c> in the engine's <c>ffi_control.rs</c>.</remarks>
public enum MirrorPause
{
    /// <summary>Sending the screen.</summary>
    Running = 0,

    /// <summary>Paused; the TV keeps showing the last picture it was sent.</summary>
    HoldingLastPicture = 1,

    /// <summary>Paused; the TV is sent one black frame.</summary>
    Black = 2,
}
