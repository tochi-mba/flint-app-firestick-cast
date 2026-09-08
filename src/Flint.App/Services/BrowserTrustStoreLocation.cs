using Flint.Session.Browser;

namespace Flint.App.Services;

/// <summary>
/// Where Flint remembers the receivers whose browser identity has been verified.
/// </summary>
/// <remarks>
/// <para>
/// One location, shared by every entry point. Verifying a television is a thing the person does
/// once, holding a code up against the screen, and it has to stay done — a second copy of this path
/// means the desktop app and the command line each keep their own idea of what is trusted, and the
/// same television gets verified twice for no reason the person could ever guess.
/// </para>
/// <para>
/// It sits alongside Flint's other durable preferences, so uninstalling or resetting Flint takes
/// the trust decisions with it rather than leaving pins behind for a future install to inherit.
/// </para>
/// </remarks>
public static class BrowserTrustStoreLocation
{
    /// <summary>The user-private file trusted receivers are remembered in.</summary>
    public static string Default => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FileOnboardingState.VendorFolder,
        FileOnboardingState.ProductFolder,
        "trusted-receivers.json");

    /// <summary>Opens the shared trust store.</summary>
    public static IBrowserTrustStore Open() => new ProtectedFileBrowserTrustStore(Default);
}
