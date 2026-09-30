using Flint.Core;

namespace Flint.Discovery;

/// <summary>
/// Finds Fire TV and Flint receiver advertisements, then identifies each one over ADB when it is
/// available.
/// </summary>
/// <remarks>
/// Discovery is advertisement-led. When multicast is filtered, a bounded fallback contacts only
/// Flint's receiver port on a derived small local subnet and requires the exact read-only receiver
/// answer. ADB ports are never swept.
/// </remarks>
public sealed class FireTvDeviceProbe : IAddressableDeviceProbe
{
    /// <summary>How long to listen for multicast answers before giving up on stragglers.</summary>
    public static readonly TimeSpan ListenWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Sends one multicast query for <paramref name="serviceType"/> and hands every datagram heard
    /// within <paramref name="window"/> to <paramref name="handle"/>, as
    /// <see cref="MulticastServiceScanner.QueryAsync"/> does on the real network.
    /// </summary>
    internal delegate Task MulticastQuery(
        string serviceType,
        TimeSpan window,
        Action<ReadOnlyMemory<byte>> handle,
        CancellationToken cancellationToken);

    private readonly AdbProbeClient _adb;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ServiceInstance>>> _advertisements;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReceiverProbeScanner.Answer>>> _probeScan;
    private readonly Func<System.Net.IPAddress, CancellationToken, Task<AdbProbeResult>> _identify;

    /// <summary>Discovery on the real network.</summary>
    /// <param name="adb">The ADB probe used to identify each device found.</param>
    public FireTvDeviceProbe(AdbProbeClient? adb = null)
        : this(adb ?? new AdbProbeClient(), ListenAsync, ReceiverProbeScanner.ScanAsync, identify: null)
    {
    }

    /// <summary>
    /// Discovery with each network step supplied, so the order of the steps and what each finding
    /// becomes can be tested without multicast, a subnet sweep or a television.
    /// </summary>
    internal FireTvDeviceProbe(
        AdbProbeClient adb,
        Func<CancellationToken, Task<IReadOnlyList<ServiceInstance>>> advertisements,
        Func<CancellationToken, Task<IReadOnlyList<ReceiverProbeScanner.Answer>>> probeScan,
        Func<System.Net.IPAddress, CancellationToken, Task<AdbProbeResult>>? identify)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _advertisements = advertisements ?? throw new ArgumentNullException(nameof(advertisements));
        _probeScan = probeScan ?? throw new ArgumentNullException(nameof(probeScan));
        _identify = identify ?? ((address, token) => _adb.ProbeAsync(address, token));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var advertised = await _advertisements(cancellationToken).ConfigureAwait(false);
        if (advertised.Count == 0)
        {
            var probed = await _probeScan(cancellationToken).ConfigureAwait(false);
            if (probed.Count == 0)
            {
                return [];
            }

            return await Task.WhenAll(
                probed.Select(answer => IdentifyAsync(answer, cancellationToken)))
                .ConfigureAwait(false);
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
    public static async Task<BrowserReceiverEvidence?> TryDiscoverBrowserEvidenceAsync(
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
        var probe = await _identify(instance.Address, cancellationToken).ConfigureAwait(false);
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
    private static Task<IReadOnlyList<ServiceInstance>> ListenAsync(CancellationToken cancellationToken) =>
        ListenAsync(MulticastServiceScanner.QueryAsync, cancellationToken);

    /// <summary>
    /// Asks for both advertisements through <paramref name="query"/> and merges what answers. A
    /// parameter so a test can play the datagrams rather than wait on a real multicast group.
    /// </summary>
    internal static async Task<IReadOnlyList<ServiceInstance>> ListenAsync(
        MulticastQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var serviceTypes = new[]
        {
            MulticastDnsCodec.FireTvServiceType,
            MulticastDnsCodec.FlintReceiverServiceType,
        };
        var answers = await Task.WhenAll(
            serviceTypes.Select(serviceType => ListenAsync(query, serviceType, cancellationToken)))
            .ConfigureAwait(false);
        return Merge(answers);
    }

    /// <summary>One instance per address, later lists winning.</summary>
    /// <remarks>
    /// The Flint announcement is listed last and therefore wins when a TV advertises both. It
    /// carries the receiver and browser ports that the Amazon advertisement cannot know about.
    /// </remarks>
    internal static IReadOnlyList<ServiceInstance> Merge(IEnumerable<IReadOnlyList<ServiceInstance>> answersInPriorityOrder)
    {
        var found = new Dictionary<string, ServiceInstance>(StringComparer.OrdinalIgnoreCase);
        foreach (var instance in answersInPriorityOrder.SelectMany(static answer => answer))
        {
            found[instance.Address.ToString()] = instance;
        }

        return [.. found.Values];
    }

    private async Task<FireTvDevice> IdentifyAsync(
        ReceiverProbeScanner.Answer answer,
        CancellationToken cancellationToken)
    {
        var probe = await _identify(answer.Address, cancellationToken).ConfigureAwait(false);
        var model = probe.Model ?? probe.Banner?.Model ?? answer.ModelName;
        return ToDevice(
            answer.Address,
            answer.ModelName,
            DiscoverySource.ReceiverProbe,
            model,
            probe);
    }

    private static async Task<IReadOnlyList<ServiceInstance>> ListenAsync(
        MulticastQuery query,
        string serviceType,
        CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, ServiceInstance>(StringComparer.OrdinalIgnoreCase);
        var gate = new Lock();

        await query(
            serviceType,
            ListenWindow,
            datagram => Record(datagram, serviceType, found, gate),
            cancellationToken).ConfigureAwait(false);

        return [.. found.Values];
    }

    /// <summary>
    /// Adds every instance of <paramref name="serviceType"/> in one received datagram to
    /// <paramref name="found"/>, keyed by address.
    /// </summary>
    /// <remarks>
    /// Runs on the receive path for every packet on the group, so a packet Flint cannot read is
    /// skipped rather than allowed to end the listen: other responders advertise plenty that is
    /// not DNS Flint understands.
    /// </remarks>
    internal static void Record(
        ReadOnlyMemory<byte> datagram,
        string serviceType,
        Dictionary<string, ServiceInstance> found,
        Lock gate)
    {
        try
        {
            var instances = MulticastDnsCodec.ReadInstances(datagram.Span, serviceType);
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
    }
}
