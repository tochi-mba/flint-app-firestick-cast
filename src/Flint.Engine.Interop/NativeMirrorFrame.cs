using System.Runtime.InteropServices;

namespace Flint.Engine.Interop;

/// <summary>
/// Blittable mirror of <c>FlintMirrorFrame</c> in the Rust ABI.
/// </summary>
/// <remarks>
/// Twenty-four bytes, eight-byte aligned. The reserved fields exist so the padding is explicit on
/// both sides rather than left to each compiler.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeMirrorFrame
{
    internal int Kind;
    internal byte KeyFrame;

    private byte _reserved0;
    private byte _reserved1;
    private byte _reserved2;

    internal long PresentationTimeUs;
    internal uint ByteCount;

    private uint _reserved3;
}
