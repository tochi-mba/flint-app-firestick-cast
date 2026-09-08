using Flint.Core;

namespace Flint.Discovery;

/// <summary>
/// Finds Fire TV devices by multicast advertisement, then identifies each one over ADB.
/// </summary>
/// <remarks>
/// Discovery is advertisement-led rather than scan-led. Sweeping a subnet for open ADB ports would
/// mean thousands of connection attempts against machines that never asked to be probed; listening
/// for devices that advertise themselves finds the same televisions without touching anything else
/// on the network.
/// </remarks>
/// <param name="adb">The ADB probe used to identify each advertised device.</param>
public sealed class FireTvDeviceProbe(AdbProbeClient? adb = null) : IAddressableDeviceProbe
{
    /// <summary>How long to listen for multicast answers before giving up on stragglers.</summary>
    public static readonly TimeSpan ListenWindow = TimeSpan.FromSeconds(3);

    private readonly AdbProbeClient _adb = adb ?? new AdbProbeClient();

    /// <inheritdoc />
    public async Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var advertised = await ListenAsync(cancellationToken).ConfigureAwait(false);
        if (advertised.Count == 0)
        {
            return [];
        }

        var identified = await Task.WhenAll(
            advertised.Select(instance => IdentifyAsync(instance, cancellationToken)))
            .ConfigureAwait(false);

        return identified;
    }

    /// <inheritdoc />
    public async Task<FireTvDevice> ProbeAddressAsync(
        System.Net.IPAddress address,
        int? port = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (port is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(port.Value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(port.Value, 65535);
        }

        var probe = port is { } explicitPort
            ? await _adb.ProbePortAsync(address, explicitPort, cancellationToken).ConfigureAwait(false)
            : await _adb.ProbeAsync(address, cancellationToken).ConfigureAwait(false);
        var model = probe.Model ?? probe.Banner?.Model;
        var friendlyName = probe.Banner?.Name ?? model ?? address.ToString();

        return ToDevice(
            address,
            friendlyName,
            DiscoverySource.Manual,
            model,
            probe);
    }

    /// <summary>
    /// Listens briefly for a multicast advertisement from <paramref name="address"/> and returns
    /// its browser endpoint evidence when present.
    /// </summary>
    public async Task<BrowserReceiverEvidence?> TryDiscoverBrowserEvidenceAsync(
        System.Net.IPAddress address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        var advertised = await ListenAsync(cancellationToken).ConfigureAwait(false);
        var match = advertised.FirstOrDefault(instance => instance.Address.Equals(address));
        return match is null ? null : BrowserDiscoveryEvidence.FromServiceInstance(match);
    }

    /// <summary>
    /// Identifies one advertised instance.
    /// </summary>
    /// <remarks>
    /// A device that fails the ADB probe is still returned, carrying
    /// <see cref="FireTvPlatform.Unknown"/>. Knowing a television is present but unidentifiable is
    /// exactly the ambiguous case the report exists to explain.
    /// </remarks>
    private async Task<FireTvDevice> IdentifyAsync(
        ServiceInstance instance,
        CancellationToken cancellationToken)
    {
        var probe = await _adb.ProbeAsync(instance.Address, cancellationToken).ConfigureAwait(false);
        var friendlyName = instance.Attribute("fn")
            ?? instance.Attribute("n")
            ?? instance.InstanceName;
        var model = probe.Model ?? probe.Banner?.Model ?? instance.Attribute("md");

        return ToDevice(
            instance.Address,
            friendlyName,
            DiscoverySource.MulticastDns,
            model,
            probe,
            BrowserDiscoveryEvidence.FromServiceInstance(instance));
    }

    private static FireTvDevice ToDevice(
        System.Net.IPAddress address,
        string friendlyName,
        DiscoverySource source,
        string? model,
        AdbProbeResult probe,
        BrowserReceiverEvidence? browserEvidence = null) =>
        new(address, friendlyName, source)
        {
            AdbPort = probe.Port,
            AdbState = probe.State,

            Platform = FireTvPlatformResolver.Resolve(probe, probe.AndroidApiLevel, model),
            Model = model,
            AndroidRelease = probe.AndroidRelease,
            AndroidApiLevel = probe.AndroidApiLevel,
            BrowserEvidence = browserEvidence,
        };

    /// <summary>
    /// Collects every Fire TV advertisement seen within the listen window.
    /// </summary>
    private static async Task<IReadOnlyList<ServiceInstance>> ListenAsync(
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, ServiceInstance>(StringComparer.OrdinalIgnoreCase);
        var gate = new Lock();

        await MulticastServiceScanner.QueryAsync(
            MulticastDnsCodec.FireTvServiceType,
            ListenWindow,
            datagram =>
            {
                try
                {
                    var instances = MulticastDnsCodec.ReadInstances(
                        datagram.Span,
                        MulticastDnsCodec.FireTvServiceType);

                    lock (gate)
                    {
                        foreach (var instance in instances)
                        {
                            found[instance.Address.ToString()] = instance;
                        }
                    }
                }
                catch (DnsFormatException)
                {
                    // Another responder on the group sent something Flint cannot read. Keep going.
                }
            },
            cancellationToken).ConfigureAwait(false);

        return [.. found.Values];
    }
}
