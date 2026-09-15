namespace Flint.Core;

/// <summary>The Windows API used to acquire desktop frames.</summary>
public enum CaptureApi
{
    /// <summary>
    /// Windows Graphics Capture. Works across adapters, so it is the default on hybrid-GPU hosts
    /// where the display and the good encoder live on different devices.
    /// </summary>
    WindowsGraphicsCapture = 0,

    /// <summary>
    /// DXGI Desktop Duplication. Slightly cheaper and provides dirty-rectangle metadata, but
    /// requires the capture and display adapters to be the same device.
    /// </summary>
    DesktopDuplication = 1,
}
