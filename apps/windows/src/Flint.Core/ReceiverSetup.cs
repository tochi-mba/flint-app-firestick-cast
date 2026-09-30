namespace Flint.Core;

/// <summary>The receiver package this copy of Flint carries for the television.</summary>
/// <param name="PackageName">The Android package name, which is what a person can check afterwards.</param>
/// <param name="VersionName">The human version, as the TV's own settings show it.</param>
/// <param name="VersionCode">The number Android compares to decide whether an install is an update.</param>
/// <param name="SizeBytes">How much will be copied to the television.</param>
public sealed record BundledReceiver(
    string PackageName,
    string VersionName,
    long VersionCode,
    long SizeBytes)
{
    /// <summary>The receiver as released.</summary>
    public const string ReleasePackage = "com.rextechnologies.flint.receiver";

    /// <summary>The receiver as a local or pull-request build names it.</summary>
    public const string DebugPackage = ReleasePackage + ".debug";

    /// <summary>Every package name a Flint receiver is known under, for asking a television which it has.</summary>
    public static readonly IReadOnlySet<string> KnownPackages = new HashSet<string>(StringComparer.Ordinal)
    {
        ReleasePackage,
        DebugPackage,
    };

    /// <summary>The activity to open once the package is installed, in every package it ships under.</summary>
    public const string MainActivity = "com.rextechnologies.flint.receiver.ReceiverActivity";

    /// <summary>The size a person sees above "this is what will be installed".</summary>
    public string SizeLabel => $"{SizeBytes / (1024.0 * 1024.0):F1} MB";

    /// <summary>Debug builds carry a suffixed package, and a person is entitled to know that before installing.</summary>
    public bool IsDebugPackage => PackageName.EndsWith(".debug", StringComparison.Ordinal);
}

/// <summary>The receiver package the television itself reported. Missing version fields stay unknown.</summary>
public sealed record InstalledReceiver(string PackageName, string? VersionName, long? VersionCode)
{
    /// <summary>The version as a person would read it, or a plain statement that the TV gave none.</summary>
    public string VersionLabel => (VersionName, VersionCode) switch
    {
        ({ } name, { } code) => $"{name} ({code})",
        ({ } name, null) => name,
        (null, { } code) => $"build {code}",
        _ => "version unknown",
    };
}

/// <summary>What the receiver button should do next on this television.</summary>
public enum ReceiverSetupAction
{
    /// <summary>Nothing is known yet: ADB has not identified the television.</summary>
    Unknown,

    /// <summary>Android is there and Flint is not.</summary>
    Install,

    /// <summary>The same package is there and older than the bundled one.</summary>
    Update,

    /// <summary>The bundled build is on the television; open it.</summary>
    Open,

    /// <summary>A newer build is on the television. Open it; never downgrade it.</summary>
    OpenNewer,

    /// <summary>A Flint package is there but the TV did not say which version. Open it.</summary>
    OpenUnverified,

    /// <summary>A Flint package under another name is there. It cannot be updated in place.</summary>
    OpenDifferentPackage,

    /// <summary>Flint is not on the television and this copy of Flint carries nothing to put there.</summary>
    NothingBundled,
}

/// <summary>
/// Decides what the receiver button does, from what the television said and what this build carries.
/// </summary>
/// <remarks>
/// A table with a test rather than a consequence of which method ran last. Nothing is installed,
/// replaced or removed on the strength of a guess: an unknown version is opened, not updated, and a
/// newer build is never replaced by an older one.
/// </remarks>
public static class ReceiverSetup
{
    /// <summary>Decides the next action.</summary>
    public static ReceiverSetupAction Decide(BundledReceiver? bundled, InstalledReceiver? installed)
    {
        if (installed is null)
        {
            return bundled is null ? ReceiverSetupAction.NothingBundled : ReceiverSetupAction.Install;
        }

        if (bundled is null || !string.Equals(installed.PackageName, bundled.PackageName, StringComparison.Ordinal))
        {
            return bundled is null ? ReceiverSetupAction.OpenUnverified : ReceiverSetupAction.OpenDifferentPackage;
        }

        return installed.VersionCode switch
        {
            null => ReceiverSetupAction.OpenUnverified,
            var code when code < bundled.VersionCode => ReceiverSetupAction.Update,
            var code when code > bundled.VersionCode => ReceiverSetupAction.OpenNewer,
            _ => ReceiverSetupAction.Open,
        };
    }

    /// <summary>The label on the one receiver button.</summary>
    public static string ActionLabel(ReceiverSetupAction action) => action switch
    {
        ReceiverSetupAction.Install => "INSTALL FLINT ON TV",
        ReceiverSetupAction.Update => "UPDATE FLINT ON TV",
        _ => "OPEN RECEIVER ON TV",
    };

    /// <summary>Whether the action copies a package to the television.</summary>
    public static bool Installs(ReceiverSetupAction action) =>
        action is ReceiverSetupAction.Install or ReceiverSetupAction.Update;

    /// <summary>Whether the button can be pressed at all.</summary>
    public static bool IsActionable(ReceiverSetupAction action) =>
        action is not ReceiverSetupAction.Unknown and not ReceiverSetupAction.NothingBundled;

    /// <summary>What the card says above the button.</summary>
    public static string Describe(
        ReceiverSetupAction action,
        BundledReceiver? bundled,
        InstalledReceiver? installed,
        string deviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        return action switch
        {
            ReceiverSetupAction.Unknown =>
                "Flint has not identified this TV over ADB yet, so it cannot say whether the receiver is installed.",
            ReceiverSetupAction.NothingBundled =>
                $"Flint is not on {deviceName}, and this copy of Flint carries no receiver to install. "
                + "Download Flint again from the GitHub release; builds published there carry the matching receiver.",
            ReceiverSetupAction.Install =>
                $"Flint is not on {deviceName} yet. This PC can install it over ADB: {Disclosure(bundled!)}",
            ReceiverSetupAction.Update =>
                $"{installed!.VersionLabel} is on {deviceName}. This PC carries "
                + $"{bundled!.VersionName} ({bundled.VersionCode}) and can update it in place: {Disclosure(bundled)}",
            ReceiverSetupAction.Open =>
                $"Flint {installed!.VersionLabel} is on {deviceName} and matches this PC.",
            ReceiverSetupAction.OpenNewer =>
                $"Flint {installed!.VersionLabel} is on {deviceName}. It is newer than the "
                + $"{bundled!.VersionName} ({bundled.VersionCode}) this PC carries, so Flint will not downgrade it.",
            ReceiverSetupAction.OpenUnverified =>
                $"{installed!.PackageName} is on {deviceName}, but the TV did not report a version to compare.",
            ReceiverSetupAction.OpenDifferentPackage =>
                $"{installed!.PackageName} {installed.VersionLabel} is on {deviceName}. This PC carries "
                + $"{bundled!.PackageName}, a different package, so it cannot replace it as an update. "
                + "Remove the other one on the TV first if you want this PC's build there.",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown receiver action."),
        };
    }

    /// <summary>Exactly what will be installed, before it is.</summary>
    private static string Disclosure(BundledReceiver bundled)
    {
        var note = bundled.IsDebugPackage ? " (a debug build, so its package name ends in .debug)" : string.Empty;
        return $"{bundled.PackageName} {bundled.VersionName} ({bundled.VersionCode}), {bundled.SizeLabel}, "
            + $"built from the same commit as this PC's Flint{note}.";
    }
}
