using System.Runtime.InteropServices;

namespace Flint.Engine.Interop;

/// <summary>
/// Blittable mirror of <c>FlintMirrorPacing</c> in the Rust ABI.
/// </summary>
/// <remarks>Sixteen bytes, eight-byte aligned.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMirrorPacing
{
    internal ulong FramesHeldBack;
    internal uint FrameRateCap;
    internal uint Reserved;
}
