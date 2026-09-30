using Flint.Core;
using Flint.Discovery;

namespace Flint.App.Services;

/// <summary>Opens the receiver over ADB, in whichever package the television has it under.</summary>
public sealed class AdbReceiverLauncher(AdbProbeClient? adbClient = null) : IReceiverLauncher
{
    private readonly AdbProbeClient adb = adbClient ?? new AdbProbeClient();

    /// <inheritdoc />
    public Task LaunchAsync(FireTvDevice device, string packageName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        if (device.AdbState is not AdbConnectionState.Connected || device.AdbPort is not { } port)
        {
            throw new InvalidOperationException("Authorize Flint in the Fire TV ADB prompt before opening the receiver.");
        }

        return adb.LaunchActivityAsync(
            device.Address,
            port,
            packageName,
            BundledReceiver.MainActivity,
            cancellationToken);
    }
}
