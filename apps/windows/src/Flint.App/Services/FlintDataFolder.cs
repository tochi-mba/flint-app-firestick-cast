namespace Flint.App.Services;

/// <summary>Where Flint keeps what it remembers, and where it writes its log.</summary>
/// <remarks>
/// Named once so the Settings page's "open the folder" buttons and its description of what is kept
/// cannot drift from where the stores actually write.
/// </remarks>
public static class FlintDataFolder
{
    /// <summary>The folder every remembered setting, TV and profile is kept in.</summary>
    public static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        FileOnboardingState.VendorFolder,
        FileOnboardingState.ProductFolder);

    /// <summary>The folder the session log is written to.</summary>
    /// <remarks>Matches <see cref="DevFileLog.Start"/>, which predates this class.</remarks>
    public static string LogsPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Flint",
        "logs");
}
