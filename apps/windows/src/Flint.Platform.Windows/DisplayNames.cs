using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Platform.Windows;

/// <summary>
/// The monitors' own names, such as "DELL U2720Q", read from Windows' display configuration.
/// </summary>
/// <remarks>
/// Capture knows a display only by its device name, <c>\\.\DISPLAY2</c>, which says nothing to a
/// person and can change when monitors are replugged. The display configuration links each device
/// name to the monitor's reported name and to its device path, which does not change.
/// </remarks>
public sealed partial class DisplayNames : IMonitorNames
{
    private const uint OnlyActivePaths = 0x2;
    private const uint GetSourceName = 1;
    private const uint GetTargetName = 2;
    private const int Success = 0;
    private const int InsufficientBuffer = 122;

    /// <summary>What a laptop's own screen is called, since it reports no name of its own.</summary>
    internal const string BuiltInName = "Built-in display";

    /// <summary>One entry of the display configuration's mode table, which is only sized here.</summary>
    private const int ModeInfoSize = 64;

    private readonly IDisplayConfigApi api;

    /// <summary>Reads names from Windows.</summary>
    public DisplayNames()
        : this(new NativeDisplayConfigApi())
    {
    }

    /// <summary>Reads names through a supplied display configuration, for tests.</summary>
    internal DisplayNames(IDisplayConfigApi api)
    {
        this.api = api;
    }

    /// <summary>The four display-configuration calls, behind an interface so their failures can be tested.</summary>
    internal interface IDisplayConfigApi
    {
        int BufferSizes(out uint pathCount, out uint modeCount);

        int Query(ref uint pathCount, PathInfo[] paths, ref uint modeCount, byte[] modes);

        int SourceName(ref SourceDeviceName request);

        int TargetName(ref TargetDeviceName request);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, MonitorName> Read()
    {
        var names = new Dictionary<string, MonitorName>(StringComparer.OrdinalIgnoreCase);
        var paths = QueryActivePaths();
        foreach (var path in paths)
        {
            if (ReadSourceName(path) is { Length: > 0 } deviceName && ReadTargetName(path) is { } monitor)
            {
                names.TryAdd(deviceName, monitor);
            }
        }

        return names;
    }

    /// <summary>The active display paths, retried while a display change races the size query.</summary>
    private PathInfo[] QueryActivePaths()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (api.BufferSizes(out var pathCount, out var modeCount) != Success)
            {
                return [];
            }

            var paths = new PathInfo[pathCount];
            var modes = new byte[checked((int)modeCount * ModeInfoSize)];
            var status = api.Query(ref pathCount, paths, ref modeCount, modes);
            if (status == Success)
            {
                return paths[..(int)pathCount];
            }

            if (status != InsufficientBuffer)
            {
                return [];
            }
        }

        return [];
    }

    private unsafe string? ReadSourceName(PathInfo path)
    {
        var request = new SourceDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = GetSourceName,
                Size = (uint)sizeof(SourceDeviceName),
                AdapterLow = path.SourceAdapterLow,
                AdapterHigh = path.SourceAdapterHigh,
                Id = path.SourceId,
            },
        };

        return api.SourceName(ref request) == Success
            ? TextUpToNul(request.GdiDeviceName, SourceDeviceName.NameCapacity)
            : null;
    }

    private unsafe MonitorName? ReadTargetName(PathInfo path)
    {
        var request = new TargetDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = GetTargetName,
                Size = (uint)sizeof(TargetDeviceName),
                AdapterLow = path.TargetAdapterLow,
                AdapterHigh = path.TargetAdapterHigh,
                Id = path.TargetId,
            },
        };

        if (api.TargetName(ref request) != Success)
        {
            return null;
        }

        var friendly = TextUpToNul(request.FriendlyName, TargetDeviceName.FriendlyNameCapacity);
        if (string.IsNullOrWhiteSpace(friendly) && IsBuiltIn(request.OutputTechnology))
        {
            friendly = BuiltInName;
        }

        return new MonitorName(friendly, TextUpToNul(request.DevicePath, TargetDeviceName.DevicePathCapacity));
    }

    /// <summary>
    /// Whether a connection is a laptop's own panel: Windows' internal kind, or embedded
    /// DisplayPort or UDI.
    /// </summary>
    internal static bool IsBuiltIn(uint outputTechnology) => outputTechnology is 0x8000_0000 or 11 or 13;

    /// <summary>The text in a fixed Windows character buffer, up to its terminator.</summary>
    internal static unsafe string TextUpToNul(char* buffer, int capacity)
    {
        var text = new ReadOnlySpan<char>(buffer, capacity);
        var end = text.IndexOf('\0');
        return new string(end >= 0 ? text[..end] : text);
    }

    /// <summary><c>DISPLAYCONFIG_PATH_INFO</c>: 72 bytes, of which only the ids are read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PathInfo
    {
        internal uint SourceAdapterLow;
        internal int SourceAdapterHigh;
        internal uint SourceId;
        internal uint SourceModeIndex;
        internal uint SourceFlags;
        internal uint TargetAdapterLow;
        internal int TargetAdapterHigh;
        internal uint TargetId;
        internal uint TargetModeIndex;
        internal uint OutputTechnology;
        internal uint Rotation;
        internal uint Scaling;
        internal uint RefreshNumerator;
        internal uint RefreshDenominator;
        internal uint ScanLineOrdering;
        internal int TargetAvailable;
        internal uint TargetFlags;
        internal uint Flags;
    }

    /// <summary><c>DISPLAYCONFIG_DEVICE_INFO_HEADER</c>: 20 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceInfoHeader
    {
        internal uint Type;
        internal uint Size;
        internal uint AdapterLow;
        internal int AdapterHigh;
        internal uint Id;
    }

    /// <summary><c>DISPLAYCONFIG_SOURCE_DEVICE_NAME</c>: 84 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SourceDeviceName
    {
        internal const int NameCapacity = 32;

        internal DeviceInfoHeader Header;
        internal fixed char GdiDeviceName[NameCapacity];
    }

    /// <summary><c>DISPLAYCONFIG_TARGET_DEVICE_NAME</c>: 420 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct TargetDeviceName
    {
        internal const int FriendlyNameCapacity = 64;
        internal const int DevicePathCapacity = 128;

        internal DeviceInfoHeader Header;
        internal uint Flags;
        internal uint OutputTechnology;
        internal ushort ManufacturerId;
        internal ushort ProductCodeId;
        internal uint ConnectorInstance;
        internal fixed char FriendlyName[FriendlyNameCapacity];
        internal fixed char DevicePath[DevicePathCapacity];
    }

    /// <summary>The real calls into <c>user32</c>.</summary>
    private sealed class NativeDisplayConfigApi : IDisplayConfigApi
    {
        public int BufferSizes(out uint pathCount, out uint modeCount) =>
            NativeMethods.GetDisplayConfigBufferSizes(OnlyActivePaths, out pathCount, out modeCount);

        public unsafe int Query(ref uint pathCount, PathInfo[] paths, ref uint modeCount, byte[] modes)
        {
            // Pinned as spans: an empty table is a null pointer and a zero count, which Windows accepts.
            fixed (PathInfo* pathPointer = paths.AsSpan())
            fixed (byte* modePointer = modes.AsSpan())
            {
                return NativeMethods.QueryDisplayConfig(OnlyActivePaths, ref pathCount, pathPointer, ref modeCount, modePointer, 0);
            }
        }

        public unsafe int SourceName(ref SourceDeviceName request)
        {
            fixed (SourceDeviceName* pointer = &request)
            {
                return NativeMethods.DisplayConfigGetDeviceInfo(&pointer->Header);
            }
        }

        public unsafe int TargetName(ref TargetDeviceName request)
        {
            fixed (TargetDeviceName* pointer = &request)
            {
                return NativeMethods.DisplayConfigGetDeviceInfo(&pointer->Header);
            }
        }
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

        [LibraryImport("user32.dll")]
        internal static unsafe partial int QueryDisplayConfig(
            uint flags,
            ref uint pathCount,
            PathInfo* paths,
            ref uint modeCount,
            byte* modes,
            nint currentTopology);

        [LibraryImport("user32.dll")]
        internal static unsafe partial int DisplayConfigGetDeviceInfo(DeviceInfoHeader* request);
    }
}
