using Flint.Core;
using Flint.Discovery;

namespace Flint.App.Services;

/// <summary>Starts the receiver APK shipped with Flint through the device's authorised ADB channel.</summary>
public sealed class AdbReceiverLauncher(AdbProbeClient? adbClient = null) : IReceiverLauncher
{
    private const string PACKAGE_NAME = "com.rextechnologies.flint.receiver.debug";
    private const string ACTIVITY_NAME = "com.rextechnologies.flint.receiver.ReceiverActivity";
    private readonly AdbProbeClient adb = adbClient ?? new AdbProbeClient();

    /// <inheritdoc />
    public Task LaunchAsync(FireTvDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.AdbState is not AdbConnectionState.Connected || device.AdbPort is not { } port)
        {
            throw new InvalidOperationException("Authorize Flint in the Fire TV ADB prompt before opening the receiver.");
        }

        return adb.LaunchActivityAsync(
            device.Address,
            port,
            PACKAGE_NAME,
            ACTIVITY_NAME,
            cancellationToken);
    }
}
