using System.Runtime.InteropServices;

namespace Flint.Engine.Interop;

/// <summary>
/// What the engine reports about screen capture on this host.
/// </summary>
/// <remarks>
/// The layout must match <c>FlintCapture</c> in the engine's <c>ffi.rs</c> byte for byte: 24 bytes,
/// eight-byte aligned. The reserved bytes exist so the padding is explicit on both sides rather
/// than left to each compiler.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct NativeCapture
{
    /// <summary>Non-zero when this host can capture the screen.</summary>
    public byte Available;

    /// <summary>The backend in use. 1 is Desktop Duplication; 0 means none.</summary>
    public byte Backend;

    private byte _reserved0;
    private byte _reserved1;
    private byte _reserved2;
    private byte _reserved3;
    private byte _reserved4;
    private byte _reserved5;

    /// <summary>Frame width in pixels, or zero when unavailable.</summary>
    public uint Width;

    /// <summary>Frame height in pixels, or zero when unavailable.</summary>
    public uint Height;

    /// <summary>The adapter frames are produced on, or zero when unavailable.</summary>
    public long AdapterLuid;
}
