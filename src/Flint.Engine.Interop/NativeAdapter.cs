using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>Blittable mirror of <c>FlintAdapter</c> in the Rust ABI.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeAdapter
{
    internal const int DescriptionCapacity = 128;

    internal long AdapterLuid;
    internal byte DrivesDisplay;
    internal byte DrivesPrimaryDisplay;
    internal byte IsSoftware;

    private byte _reserved0;

    internal ushort DescriptionLength;

    private ushort _reserved1;
    private fixed char _description[DescriptionCapacity];

    internal DisplayAdapter? ToModel()
    {
        if (DrivesDisplay > 1 || DrivesPrimaryDisplay > 1 || IsSoftware > 1
            || (DrivesPrimaryDisplay == 1 && DrivesDisplay == 0)
            || DescriptionLength > DescriptionCapacity)
        {
            return null;
        }

        string description;
        fixed (char* pointer = _description)
        {
            description = new string(pointer, 0, DescriptionLength);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = $"DXGI adapter 0x{AdapterLuid:X16}";
        }

        return new DisplayAdapter(
            AdapterLuid,
            description,
            DrivesDisplay == 1,
            IsSoftware == 1);
    }
}
