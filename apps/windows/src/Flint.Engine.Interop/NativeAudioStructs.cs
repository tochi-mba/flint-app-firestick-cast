using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>Blittable mirror of <c>FlintAudioDevice</c>: 776 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeAudioDevice
{
    internal const int NameCapacity = 128;
    internal const int IdCapacity = 256;

    internal byte IsDefault;

    private byte _reserved0;

    internal ushort NameLength;
    internal ushort IdLength;

    private ushort _reserved1;
    private fixed char _name[NameCapacity];
    private fixed char _id[IdCapacity];

    /// <summary>Sets the name and identity, for tests that build what the engine would.</summary>
    internal void Set(string name, string id)
    {
        NameLength = (ushort)Math.Min(name.Length, NameCapacity);
        IdLength = (ushort)Math.Min(id.Length, IdCapacity);
        for (var index = 0; index < NameLength; index++)
        {
            _name[index] = name[index];
        }

        for (var index = 0; index < IdLength; index++)
        {
            _id[index] = id[index];
        }
    }

    /// <summary>The output this describes, or null when the engine sent something malformed.</summary>
    internal AudioDevice? ToModel()
    {
        if (IsDefault > 1 || NameLength is 0 or > NameCapacity || IdLength is 0 or > IdCapacity)
        {
            return null;
        }

        fixed (char* name = _name)
        fixed (char* id = _id)
        {
            return new AudioDevice(new string(id, 0, IdLength), new string(name, 0, NameLength), IsDefault == 1);
        }
    }
}

/// <summary>Blittable mirror of <c>FlintAudioConfig</c>: 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeAudioConfig
{
    internal nint DeviceId;
    internal uint DeviceIdLength;
    internal uint BitrateKbps;
    internal long StartOffsetUs;
    internal uint DelayMs;
    internal uint Reserved;
}

/// <summary>Blittable mirror of <c>FlintAudioStats</c>: 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NativeAudioStats
{
    internal ulong Packets;
    internal ulong Dropped;
    internal float Level;
    internal uint State;
    internal float Meter;
    internal uint Reserved;
}
