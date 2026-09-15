namespace Flint.App.Services;

/// <summary>The one Windows-current-user location used for device-owned browser profiles.</summary>
public static class BrowserProfileLibraryStoreLocation
{
    /// <summary>
    /// Stable key for this Windows account's device-owned profile. The local, non-roaming path and
    /// DPAPI user scope provide device/account isolation; this identifier contains no fingerprint.
    /// </summary>
    public static BrowserProfileId CurrentDeviceProfile { get; } =
        new(new Guid("fa683d36-9ff9-4c13-aece-ec797f42b9a5"));

    /// <summary>Directory containing separately protected profile documents.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FileOnboardingState.VendorFolder,
        FileOnboardingState.ProductFolder,
        "browser-profiles");

    /// <summary>Opens the Windows device's durable browser-profile store.</summary>
    public static IBrowserProfileLibraryStore Open() =>
        new FileBrowserProfileLibraryStore(DefaultDirectory);
}
