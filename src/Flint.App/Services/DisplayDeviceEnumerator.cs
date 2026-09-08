using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Flint.App.Services;

/// <summary>
/// Lists the adapters Windows currently has attached to the desktop.
/// </summary>
/// <remarks>
/// The driver registry records every adapter that has a driver bound, which on a hybrid laptop
/// includes the discrete GPU whether or not it is driving anything. Only
/// <c>EnumDisplayDevices</c> reports which adapters are actually attached to the desktop, and that
/// distinction is what the capture strategy turns on: capturing from an adapter that drives no
/// display is the exact mistake that makes Desktop Duplication fail on this class of machine.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class DisplayDeviceEnumerator
{
    private const int AttachedToDesktop = 0x0000_0001;

    /// <summary>
    /// The adapter description strings currently attached to the desktop.
    /// </summary>
    /// <remarks>
    /// Returns descriptions rather than handles because they are what the registry enumeration also
    /// has, which lets the two sources be matched without a native adapter identifier.
    /// </remarks>
    internal static IReadOnlySet<string> AttachedAdapterDescriptions()
    {
        var attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (uint index = 0; ; index++)
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            if ((device.StateFlags & AttachedToDesktop) != 0 && !string.IsNullOrWhiteSpace(device.DeviceString))
            {
                attached.Add(device.DeviceString);
            }
        }

        return attached;
    }

    // Classic DllImport rather than LibraryImport: the fixed-size character arrays in
    // DISPLAY_DEVICEW are not supported by source-generated marshalling, and hand-rolling the
    // buffers would be more code than the interop saves.
    [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? device,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }
}
