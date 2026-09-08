namespace Flint.Core;

/// <summary>
/// The operating-system family of a discovered Fire TV device.
/// </summary>
/// <remarks>
/// This distinction decides whether Flint can exist on a device at all. Fire OS is Android and can
/// run a sideloaded receiver; Vega is not Android and cannot run one by any known method.
/// </remarks>
public enum FireTvPlatform
{
    /// <summary>The platform has not been determined. Never treat this as a capability.</summary>
    Unknown = 0,

    /// <summary>Fire OS 7 (Android-based). Sideloading and ADB are available.</summary>
    FireOs7 = 1,

    /// <summary>Fire OS 8 (Android-based). Sideloading and ADB are available.</summary>
    FireOs8 = 2,

    /// <summary>
    /// Vega OS. A Linux system that is not Android. It cannot install an APK by any method, so no
    /// custom receiver is possible on this device.
    /// </summary>
    Vega = 3,

    /// <summary>Fire OS 5 (Android-based). Sideloading and ADB are available.</summary>
    FireOs5 = 4,

    /// <summary>Fire OS 6 (Android-based). Sideloading and ADB are available.</summary>
    FireOs6 = 5,

    /// <summary>Fire OS 14 (Android-based). Sideloading and ADB are available.</summary>
    FireOs14 = 6,

    /// <summary>Fire OS 16 (Android-based). Sideloading and ADB are available.</summary>
    FireOs16 = 7,
}
