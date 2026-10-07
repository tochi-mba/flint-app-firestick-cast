using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>Blittable mirror of <c>FlintOutput</c> in the Rust ABI.</summary>
/// <remarks>104 bytes, eight-byte aligned.</remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeOutput
{
    internal const int DeviceNameCapacity = 32;

    internal uint Index;
    internal uint Rotation;
    internal long AdapterLuid;
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
    internal byte Attached;
    internal byte Main;

    private byte _reserved0;
    private byte _reserved1;

    internal ushort DeviceNameLength;

    private ushort _reserved2;
    private fixed char _deviceName[DeviceNameCapacity];

    /// <summary>Sets the device name, for tests that build a struct the engine would.</summary>
    internal void SetDeviceName(string name)
    {
        DeviceNameLength = (ushort)Math.Min(name.Length, DeviceNameCapacity);
        for (var index = 0; index < DeviceNameLength; index++)
        {
            _deviceName[index] = name[index];
        }
    }

    /// <summary>The display this describes, or null when the engine sent something malformed.</summary>
    internal DisplayOutput? ToModel()
    {
        if (Attached > 1 || Main > 1
            || (Main == 1 && Attached == 0)
            || Rotation is < 1 or > 4
            || DeviceNameLength is 0 or > DeviceNameCapacity
            || (long)Right - Left is <= 0 or > int.MaxValue
            || (long)Bottom - Top is <= 0 or > int.MaxValue)
        {
            return null;
        }

        string deviceName;
        fixed (char* pointer = _deviceName)
        {
            deviceName = new string(pointer, 0, DeviceNameLength);
        }

        return new DisplayOutput(
            Index,
            deviceName,
            Left,
            Top,
            Right - Left,
            Bottom - Top,
            (DisplayRotation)Rotation,
            Main == 1);
    }
}
