using Flint.Core;
using Flint.Discovery;

namespace Flint.App.Services;

/// <summary>The two things Flint does to a television's packages over ADB, without saying how.</summary>
/// <remarks>
/// An interface so the Cast page's decisions - which button to show, what to say when the
/// television refuses - can be tested with a fake that never opens a socket.
/// </remarks>
public interface IReceiverInstaller
{
    /// <summary>Read-only. Asks the television which known Flint package is on it, and its version.</summary>
    Task<InstalledReceiver?> FindInstalledAsync(
        FireTvDevice device,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken = default);

    /// <summary>Installs the package, replacing the same package in place.</summary>
    Task<AdbInstallOutcome> InstallAsync(
        FireTvDevice device,
        ReadOnlyMemory<byte> apk,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// An installer for a shell built without ADB: design-time data and tests that never open a socket.
/// </summary>
/// <remarks>
/// It reports that it cannot look, rather than that nothing is installed, so the page keeps its
/// plain "open the receiver" offer instead of claiming knowledge it never had.
/// </remarks>
public sealed class OfflineReceiverInstaller : IReceiverInstaller
{
    /// <inheritdoc />
    public Task<InstalledReceiver?> FindInstalledAsync(
        FireTvDevice device,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("ADB is not available in this configuration.");

    /// <inheritdoc />
    public Task<AdbInstallOutcome> InstallAsync(
        FireTvDevice device,
        ReadOnlyMemory<byte> apk,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("ADB is not available in this configuration.");
}

/// <summary>The real installer: ADB to the television that answered the probe.</summary>
public sealed class AdbReceiverInstaller(AdbProbeClient? adbClient = null) : IReceiverInstaller
{
    private readonly AdbProbeClient adb = adbClient ?? new AdbProbeClient();

    /// <inheritdoc />
    public Task<InstalledReceiver?> FindInstalledAsync(
        FireTvDevice device,
        IReadOnlyList<string> candidates,
        CancellationToken cancellationToken = default)
    {
        var port = AuthorizedPort(device);
        return adb.FindInstalledPackageAsync(device.Address, port, candidates, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AdbInstallOutcome> InstallAsync(
        FireTvDevice device,
        ReadOnlyMemory<byte> apk,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        var port = AuthorizedPort(device);
        return adb.InstallPackageAsync(device.Address, port, apk, progress, cancellationToken);
    }

    private static int AuthorizedPort(FireTvDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.AdbState is not AdbConnectionState.Connected || device.AdbPort is not { } port)
        {
            throw new InvalidOperationException("Authorize Flint in the Fire TV ADB prompt first.");
        }

        return port;
    }
}
