namespace Flint.Core;

/// <summary>Where a mirror session's video is encoded.</summary>
/// <remarks>
/// Values must match <c>encoder_kind</c> in the engine's <c>ffi.rs</c>; zero is never sent by the
/// engine and stands for a session that cannot say.
/// </remarks>
public enum MirrorEncoderKind
{
    /// <summary>Not reported.</summary>
    Unknown = 0,

    /// <summary>The graphics card's own encode block.</summary>
    Hardware = 1,

    /// <summary>Windows' software encoder, on the processor.</summary>
    Software = 2,
}
