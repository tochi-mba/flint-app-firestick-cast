namespace Flint.Engine.Interop;

/// <summary>
/// Status codes returned by <c>flint-engine</c>.
/// </summary>
/// <remarks>
/// Values are stable and must match <c>FlintStatus</c> in the engine's <c>ffi.rs</c>. Changing one
/// silently changes its meaning on the other side of the boundary.
/// </remarks>
public enum FlintStatus
{
    /// <summary>The call succeeded.</summary>
    Ok = 0,

    /// <summary>A required pointer was null. Always a defect in this wrapper.</summary>
    NullArgument = -1,

    /// <summary>The supplied buffer was too small; nothing was written.</summary>
    BufferTooSmall = -2,

    /// <summary>The platform refused the request.</summary>
    PlatformError = -3,

    /// <summary>The engine panicked and recovered. Always a defect in the engine.</summary>
    InternalError = -4,

    /// <summary>The caller supplied a value outside the ABI's documented range.</summary>
    InvalidArgument = -5,
}
