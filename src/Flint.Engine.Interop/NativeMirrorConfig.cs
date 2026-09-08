using System.Runtime.InteropServices;

namespace Flint.Engine.Interop;

/// <summary>
/// Blittable mirror of <c>FlintMirrorConfig</c> in the Rust ABI.
/// </summary>
/// <remarks>Sixteen bytes, four-byte aligned. The engine asserts the same layout on its side.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMirrorConfig
{
    internal uint OutputIndex;
    internal uint FrameRate;
    internal uint BitrateBitsPerSecond;
    internal uint MaxWidth;
}
