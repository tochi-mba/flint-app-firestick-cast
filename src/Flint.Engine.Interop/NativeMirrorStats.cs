using System.Runtime.InteropServices;

namespace Flint.Engine.Interop;

/// <summary>
/// Blittable mirror of <c>FlintMirrorStats</c> in the Rust ABI.
/// </summary>
/// <remarks>Forty bytes, eight-byte aligned.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMirrorStats
{
    internal ulong FramesEncoded;
    internal ulong FramesUnchanged;
    internal ulong Recoveries;
    internal ulong BytesEncoded;
    internal uint Width;
    internal uint Height;
}
