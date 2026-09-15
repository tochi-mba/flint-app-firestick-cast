using System.Net;

namespace Flint.Core;

/// <summary>
/// Finds the device, probes the host, measures the path, then judges the connection.
/// </summary>
/// <remarks>
/// Composition only. Every decision lives in <see cref="CapabilityAssessor"/>, which is pure, so
/// the interesting logic stays testable without touching a network or a GPU.
/// </remarks>
/// <param name="hostProbe">Discovers host encoders and adapters.</param>
/// <param name="deviceProbe">Discovers and identifies receivers.</param>
/// <param name="networkProbe">Measures the path to a chosen receiver.</param>
public sealed class CapabilityProber(
    IHostProbe hostProbe,
    IDeviceProbe deviceProbe,
    INetworkProbe networkProbe)
{
    /// <summary>
    /// Produces a full capability report.
    /// </summary>
    /// <remarks>
    /// The host probe and device discovery are independent, so they run concurrently. The network
    /// measurement cannot start until a device is chosen, so it follows.
    /// </remarks>
    public async Task<CapabilityReport> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var hostTask = hostProbe.ProbeAsync(cancellationToken);
        var devicesTask = deviceProbe.DiscoverAsync(cancellationToken);
        await Task.WhenAll(hostTask, devicesTask).ConfigureAwait(false);

        var host = await hostTask.ConfigureAwait(false);
        var devices = await devicesTask.ConfigureAwait(false);
        var device = SelectPrimary(devices);

        var path = device is null
            ? null
            : await networkProbe.MeasureAsync(device, cancellationToken).ConfigureAwait(false);

        return CapabilityAssessor.Assess(device, host, path);
    }

    /// <summary>Probes a receiver address explicitly supplied by the user.</summary>
    /// <remarks>
    /// The host and receiver remain independent and are probed concurrently. Only the named
    /// endpoint is touched; this is not a subnet scan.
    /// </remarks>
    public async Task<CapabilityReport> ProbeAddressAsync(
        IPAddress address,
        int? port = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (deviceProbe is not IAddressableDeviceProbe addressableProbe)
        {
            throw new NotSupportedException("The configured device probe does not accept an address.");
        }

        var hostTask = hostProbe.ProbeAsync(cancellationToken);
        var deviceTask = addressableProbe.ProbeAddressAsync(address, port, cancellationToken);
        await Task.WhenAll(hostTask, deviceTask).ConfigureAwait(false);

        var host = await hostTask.ConfigureAwait(false);
        var device = await deviceTask.ConfigureAwait(false);
        var path = device.IsReachable
            ? await networkProbe.MeasureAsync(device, cancellationToken).ConfigureAwait(false)
            : null;

        return CapabilityAssessor.Assess(device, host, path);
    }

    /// <summary>
    /// Chooses which discovered device the report is about.
    /// </summary>
    /// <remarks>
    /// A reachable device outranks an unreachable one, because a report about a device Flint can
    /// actually talk to is more useful than one about a device it merely saw advertised.
    /// </remarks>
    private static FireTvDevice? SelectPrimary(IReadOnlyList<FireTvDevice> devices) =>
        devices.FirstOrDefault(device => device.IsReachable) ?? devices.FirstOrDefault();
}
