using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Flint.Core;
using Shouldly;

namespace Flint.Discovery.Tests;

/// <summary>
/// The subnet a fallback probe may cover: the arithmetic decides which machines Flint contacts, so
/// an off-by-one here is a stranger being probed or a television being missed.
/// </summary>
public sealed class LocalSubnetTests
{
    [Theory]
    [InlineData("192.168.1.10", 24, 253)]
    [InlineData("192.168.1.1", 30, 1)]
    [InlineData("192.168.1.0", 30, 2)]
    [InlineData("10.0.0.1", 31, 1)]
    [InlineData("10.0.0.1", 32, 0)]
    [InlineData("10.0.0.1", 23, 509)]
    public void HostCount_IsTheRangeLessThisPc(string address, int prefix, long expected)
    {
        var subnet = new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse(address), prefix);

        subnet.HostCount.ShouldBe(expected);
        subnet.Hosts().Count().ShouldBe((int)expected);
    }

    [Fact]
    public void Hosts_SkipsTheNetworkBroadcastAndOwnAddress()
    {
        var subnet = new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse("192.168.7.2"), 29);

        subnet.Hosts().Select(static host => host.ToString())
            .ShouldBe(["192.168.7.1", "192.168.7.3", "192.168.7.4", "192.168.7.5", "192.168.7.6"]);
    }

    [Fact]
    public void Hosts_AtTheTopOfTheAddressSpace_EndsRatherThanWrapping()
    {
        var subnet = new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse("255.255.255.254"), 31);

        subnet.Hosts().Select(static host => host.ToString()).ShouldBe(["255.255.255.255"]);
        new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse("255.255.255.255"), 32).Hosts().ShouldBeEmpty();
    }

    [Fact]
    public void AZeroPrefix_CoversEverythingSoNothingOnItIsEverSwept()
    {
        new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse("10.0.0.1"), 0).HostCount
            .ShouldBeGreaterThan(ReceiverProbeScanner.MaximumHosts);
    }

    [Theory]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.1", true)]
    [InlineData("172.15.0.1", false)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.0.1", true)]
    [InlineData("192.169.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.1.1", false)]
    [InlineData("fd00::1", false)]
    public void IsPrivate_IsExactlyTheRfc1918Ranges(string address, bool expected)
    {
        ReceiverProbeScanner.IsPrivate(IPAddress.Parse(address)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("192.168.1.10", 24, true)]
    [InlineData("10.0.0.1", 23, true)]
    [InlineData("10.0.0.1", 22, false)]
    [InlineData("172.19.160.1", 20, false)]
    [InlineData("10.0.0.1", 32, false)]
    public void IsSweepable_IsSomebodyElseOnASmallSubnet(string address, int prefix, bool expected)
    {
        ReceiverProbeScanner.IsSweepable(new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse(address), prefix))
            .ShouldBe(expected);
    }

    [Fact]
    public void LocalSubnets_OnlyEverOffersSmallPrivateIpv4Subnets()
    {
        foreach (var subnet in ReceiverProbeScanner.LocalSubnets())
        {
            subnet.Address.AddressFamily.ShouldBe(AddressFamily.InterNetwork);
            ReceiverProbeScanner.IsPrivate(subnet.Address).ShouldBeTrue();
            subnet.HostCount.ShouldBeInRange(1, ReceiverProbeScanner.MaximumHosts);
        }
    }

    [Theory]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, "192.168.1.10", 24, true)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Wireless80211, "10.0.0.5", 24, true)]
    [InlineData(OperationalStatus.Down, NetworkInterfaceType.Ethernet, "192.168.1.10", 24, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Loopback, "127.0.0.1", 8, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Tunnel, "10.8.0.2", 24, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ppp, "10.64.0.2", 30, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Wwanpp, "10.44.201.6", 30, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Wwanpp2, "10.44.201.6", 30, false)]
    [InlineData(OperationalStatus.Up, (NetworkInterfaceType)53, "10.8.0.2", 24, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.GigabitEthernet, "192.168.1.10", 24, true)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, "fe80::1", 64, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, "8.8.4.4", 24, false)]
    [InlineData(OperationalStatus.Up, NetworkInterfaceType.Ethernet, "10.0.0.5", 16, false)]
    public void LocalSubnets_SweepsOnlyASmallPrivateSubnetOnALinkATvCouldShare(
        OperationalStatus status,
        NetworkInterfaceType type,
        string address,
        int prefix,
        bool swept)
    {
        var subnets = ReceiverProbeScanner.LocalSubnets(
            [new ReceiverProbeScanner.AdapterAddress(status, type, IPAddress.Parse(address), prefix)]);

        subnets.Any().ShouldBe(swept);
    }

    [Fact]
    public void LocalSubnets_OffersASubnetListedTwiceOnce()
    {
        var wifi = new ReceiverProbeScanner.AdapterAddress(
            OperationalStatus.Up,
            NetworkInterfaceType.Wireless80211,
            IPAddress.Parse("192.168.1.10"),
            24);

        ReceiverProbeScanner.LocalSubnets([wifi, wifi])
            .ShouldBe([new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse("192.168.1.10"), 24)]);
    }
}

/// <summary>
/// The fallback probe, run for real against receivers on loopback addresses: only an exact answer
/// counts, and every other kind of peer - silent, rude, absent - is simply not a receiver.
/// </summary>
public sealed class ReceiverProbeScannerScanTests
{
    // 127.0.0.1/29 covers .2 to .6: five hosts, each a different kind of peer.
    private static readonly ReceiverProbeScanner.LocalSubnet Loopback29 =
        new(IPAddress.Parse("127.0.0.1"), 29);

    [Fact]
    public async Task OnlyTheHostGivingTheExactAnswerIsFound()
    {
        using var receiver = Listener("127.0.0.2", port: 0);
        var port = ((IPEndPoint)receiver.LocalEndpoint).Port;
        using var stranger = Listener("127.0.0.3", port);
        using var mute = Listener("127.0.0.4", port);
        using var unterminated = Listener("127.0.0.5", port);
        // 127.0.0.6 has nothing listening at all.
        var peers = Task.WhenAll(
            AnswerAsync(receiver, "REXCAST RECEIVER/1\tLiving Room\t47855\n"),
            AnswerAsync(stranger, "SSH-2.0-OpenSSH_9.6\n"),
            HoldOpenAsync(mute),
            AnswerAsync(unterminated, "REXCAST RECEIVER/1\tLiving Room\t47855"));

        var answers = await ReceiverProbeScanner.ScanAsync(
            [Loopback29, Loopback29],
            port,
            TimeSpan.FromMilliseconds(500),
            TestContext.Current.CancellationToken);

        var answer = answers.ShouldHaveSingleItem("the same subnet twice still finds the TV once");
        answer.Address.ShouldBe(IPAddress.Parse("127.0.0.2"));
        answer.ModelName.ShouldBe("Living Room");
        answer.ServicePort.ShouldBe(47_855);
        StopAll(receiver, stranger, mute, unterminated);
        await peers;
    }

    [Fact]
    public async Task ACancelledScan_IsCancelledRatherThanReportedEmpty()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => ReceiverProbeScanner.ScanAsync(
            [Loopback29],
            ReceiverProbeScanner.Port,
            TimeSpan.FromMilliseconds(500),
            cancelled.Token));
    }

    [Fact]
    public async Task CancellingAScanMidway_IsCancelledRatherThanReportedEmpty()
    {
        // Every host accepts and says nothing, so every probe is mid-wait when the caller cancels.
        // The probe has to tell that apart from its own per-host timeout, which means "not a TV".
        using var first = Listener("127.0.0.2", port: 0);
        var port = ((IPEndPoint)first.LocalEndpoint).Port;
        using var second = Listener("127.0.0.3", port);
        using var third = Listener("127.0.0.4", port);
        using var fourth = Listener("127.0.0.5", port);
        using var fifth = Listener("127.0.0.6", port);
        var holding = Task.WhenAll(
            HoldOpenAsync(first),
            HoldOpenAsync(second),
            HoldOpenAsync(third),
            HoldOpenAsync(fourth),
            HoldOpenAsync(fifth));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => ReceiverProbeScanner.ScanAsync(
            [Loopback29],
            port,
            TimeSpan.FromSeconds(10),
            cancel.Token));

        StopAll(first, second, third, fourth, fifth);
        await holding;
    }

    [Fact]
    public async Task AnAddressThisPcDoesNotHave_ProbesNothingAndFindsNothing()
    {
        // Binding the probe to an address no interface holds fails at once, for every host.
        var answers = await ReceiverProbeScanner.ScanAsync(
            [new ReceiverProbeScanner.LocalSubnet(IPAddress.Parse("192.0.2.1"), 30)],
            ReceiverProbeScanner.Port,
            TimeSpan.FromMilliseconds(500),
            TestContext.Current.CancellationToken);

        answers.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheRealScan_WhenCancelled_ContactsNobody()
    {
        // Cancelled before it starts, so this unit test never probes the developer's own network.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        try
        {
            (await ReceiverProbeScanner.ScanAsync(cancelled.Token)).ShouldBeEmpty();
        }
        catch (OperationCanceledException)
        {
            // Equally correct: a machine with a sweepable subnet stops at its first probe.
        }
    }

    [Fact]
    public async Task NoSubnets_FindsNothing()
    {
        var answers = await ReceiverProbeScanner.ScanAsync(
            [],
            ReceiverProbeScanner.Port,
            TimeSpan.FromMilliseconds(500),
            TestContext.Current.CancellationToken);

        answers.ShouldBeEmpty();
    }

    private static TcpListener Listener(string address, int port)
    {
        var listener = new TcpListener(IPAddress.Parse(address), port);
        listener.Start();
        return listener;
    }

    /// <summary>Answers each probe with <paramref name="reply"/>, then hangs up.</summary>
    private static async Task AnswerAsync(TcpListener listener, string reply)
    {
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var request = new byte[64];
                _ = await stream.ReadAsync(request);
                await stream.WriteAsync(Encoding.UTF8.GetBytes(reply));
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or IOException)
        {
            // The listener was stopped at the end of the test.
        }
    }

    /// <summary>Accepts each probe and says nothing, until the prober gives up.</summary>
    private static async Task HoldOpenAsync(TcpListener listener)
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(async () =>
                {
                    using (client)
                    {
                        var sink = new byte[64];
                        try
                        {
                            while (await client.GetStream().ReadAsync(sink) > 0)
                            {
                            }
                        }
                        catch (IOException)
                        {
                        }
                    }
                });
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
        }
    }

    private static void StopAll(params TcpListener[] listeners)
    {
        foreach (var listener in listeners)
        {
            listener.Stop();
        }
    }
}

/// <summary>
/// Discovery's order of work: advertisements first, the probe only when nothing advertised, and
/// each finding labelled with how it was found.
/// </summary>
public sealed class FireTvDeviceProbeDiscoveryTests
{
    private static readonly IPAddress Tv = IPAddress.Parse("192.168.1.42");

    [Fact]
    public async Task AnAdvertisedTv_IsIdentifiedWithoutEverRunningTheProbe()
    {
        var scans = 0;
        var probe = Probe(
            advertisements: [Advertised(Tv, "fn=Living Room")],
            probeScan: () =>
            {
                scans++;
                return [];
            });

        var device = (await probe.DiscoverAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        scans.ShouldBe(0);
        device.Source.ShouldBe(DiscoverySource.MulticastDns);
        device.FriendlyName.ShouldBe("Living Room");
        device.AdbState.ShouldBe(AdbConnectionState.Connected);
        device.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public async Task NothingAdvertised_FallsBackToTheProbeAndLabelsWhatItFound()
    {
        var probe = Probe(
            advertisements: [],
            probeScan: () => [new ReceiverProbeScanner.Answer(Tv, "Bedroom", 47_855)],
            adb: new AdbProbeResult(AdbConnectionState.Refused, Port: null, Banner: null));

        var device = (await probe.DiscoverAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        device.Source.ShouldBe(DiscoverySource.ReceiverProbe);
        device.Address.ShouldBe(Tv);
        device.FriendlyName.ShouldBe("Bedroom");
        device.Model.ShouldBe("Bedroom", "with ADB refused, the receiver's own name is the only model known");
        device.AdbState.ShouldBe(AdbConnectionState.Refused);
    }

    [Fact]
    public async Task AProbedTvWithOnlyABanner_TakesItsModelFromTheBanner()
    {
        var probe = Probe(
            advertisements: [],
            probeScan: () => [new ReceiverProbeScanner.Answer(Tv, "Bedroom", 47_855)],
            adb: new AdbProbeResult(AdbConnectionState.Connected, 5555, AdbBanner.Parse("device::ro.product.model=AFTMM")));

        var device = (await probe.DiscoverAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        device.Model.ShouldBe("AFTMM");
        device.FriendlyName.ShouldBe("Bedroom");
    }

    [Fact]
    public void ThePublicConstructor_BuildsItsOwnAdbClient()
    {
        Should.NotThrow(() => new FireTvDeviceProbe());
    }

    [Fact]
    public async Task BrowserEvidence_ForATvThatDoesNotAdvertise_IsNull()
    {
        // Listens on the real group for the full window; loopback never advertises, so nothing matches.
        var evidence = await FireTvDeviceProbe.TryDiscoverBrowserEvidenceAsync(IPAddress.Loopback, TestContext.Current.CancellationToken);

        evidence.ShouldBeNull();
    }

    [Fact]
    public async Task NothingAdvertisedAndNothingAnswering_FindsNothing()
    {
        var probe = Probe(advertisements: [], probeScan: () => []);

        (await probe.DiscoverAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public void Merge_KeepsOneInstancePerAddressAndLetsTheFlintAdvertisementWin()
    {
        var amazon = Advertised(Tv, "fn=Amazon name");
        var flint = Advertised(Tv, "browser_port=44321");
        var other = Advertised(IPAddress.Parse("192.168.1.50"), "fn=Other");

        var merged = FireTvDeviceProbe.Merge([[amazon, other], [flint]]);

        merged.Count.ShouldBe(2);
        merged.Single(instance => instance.Address.Equals(Tv)).ShouldBeSameAs(flint);
    }

    [Fact]
    public void Record_AddsTheInstancesInADatagramAndSkipsOnesItCannotRead()
    {
        const string serviceType = MulticastDnsCodec.FlintReceiverServiceType;
        const string instance = "Living Room._rexcast._tcp.local";
        var packet = new DnsPacketBuilder()
            .AddPointer(serviceType, instance)
            .AddService(instance, "tv.local", 47_855)
            .AddAddress("tv.local", "192.168.1.42")
            .Build();
        var found = new Dictionary<string, ServiceInstance>(StringComparer.OrdinalIgnoreCase);
        var gate = new Lock();

        FireTvDeviceProbe.Record(packet, serviceType, found, gate);
        FireTvDeviceProbe.Record(new byte[] { 1, 2, 3 }, serviceType, found, gate);

        found.Keys.ShouldBe(["192.168.1.42"]);
        found["192.168.1.42"].Port.ShouldBe(47_855);
    }

    [Fact]
    public async Task Listen_AsksForBothAdvertisementsAndLetsFlintsWinForTheSameTv()
    {
        var asked = new System.Collections.Concurrent.ConcurrentQueue<(string ServiceType, TimeSpan Window)>();

        var heard = await FireTvDeviceProbe.ListenAsync(
            (serviceType, window, handle, _) =>
            {
                asked.Enqueue((serviceType, window));
                if (serviceType == MulticastDnsCodec.FireTvServiceType)
                {
                    handle(Advertisement(serviceType, "Lounge TV", 8009, "192.168.1.60"));
                    handle(Advertisement(serviceType, "Bedroom TV", 8009, "192.168.1.61"));
                }
                else
                {
                    handle(new byte[] { 1, 2, 3 });
                    handle(Advertisement(serviceType, "Lounge", 47_855, "192.168.1.60"));
                }

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        asked.OrderBy(static query => query.ServiceType, StringComparer.Ordinal).ShouldBe(
        [
            (MulticastDnsCodec.FireTvServiceType, FireTvDeviceProbe.ListenWindow),
            (MulticastDnsCodec.FlintReceiverServiceType, FireTvDeviceProbe.ListenWindow),
        ]);
        // The Lounge TV advertises both; Flint's answer carries the receiver's port, so it wins.
        heard.Select(static instance => (instance.Address.ToString(), instance.Port))
            .OrderBy(static pair => pair.Item1, StringComparer.Ordinal)
            .ShouldBe([("192.168.1.60", 47_855), ("192.168.1.61", 8009)]);
    }

    [Fact]
    public async Task Listen_WithoutAQuery_IsRefused()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => FireTvDeviceProbe.ListenAsync(null!, TestContext.Current.CancellationToken));
    }

    private static byte[] Advertisement(string serviceType, string name, int port, string address)
    {
        var instance = $"{name}.{serviceType}";
        var host = $"{name.Replace(' ', '-')}.local";
        return new DnsPacketBuilder()
            .AddPointer(serviceType, instance)
            .AddService(instance, host, port)
            .AddAddress(host, address)
            .Build();
    }

    [Fact]
    public void TheInternalConstructor_RejectsMissingSteps()
    {
        var adb = Client();
        Should.Throw<ArgumentNullException>(() => new FireTvDeviceProbe(null!, _ => Empty<ServiceInstance>(), _ => Empty<ReceiverProbeScanner.Answer>(), null));
        Should.Throw<ArgumentNullException>(() => new FireTvDeviceProbe(adb, null!, _ => Empty<ReceiverProbeScanner.Answer>(), null));
        Should.Throw<ArgumentNullException>(() => new FireTvDeviceProbe(adb, _ => Empty<ServiceInstance>(), null!, null));
    }

    [Fact]
    public async Task WithoutAnIdentifyStep_TheRealAdbClientIsAsked()
    {
        // The real client's port sweep checks for cancellation before its first connection. The
        // scan cancels once it has an answer, so reaching that check proves the client was called
        // without sitting through a sweep of thirty-one ports on an address nobody answers.
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var probe = new FireTvDeviceProbe(
            Client(),
            _ => Task.FromResult<IReadOnlyList<ServiceInstance>>([]),
            _ =>
            {
                cancel.Cancel();
                return Task.FromResult<IReadOnlyList<ReceiverProbeScanner.Answer>>([new(IPAddress.Parse("192.0.2.1"), "Den", 47_855)]);
            },
            identify: null);

        await Should.ThrowAsync<OperationCanceledException>(() => probe.DiscoverAsync(cancel.Token));
    }

    private static FireTvDeviceProbe Probe(
        IReadOnlyList<ServiceInstance> advertisements,
        Func<IReadOnlyList<ReceiverProbeScanner.Answer>> probeScan,
        AdbProbeResult? adb = null)
    {
        var result = adb ?? new AdbProbeResult(
            AdbConnectionState.Connected,
            5555,
            AdbBanner.Parse("device::ro.product.model=AFTKA"),
            AndroidApiLevel: 30,
            AndroidRelease: "11",
            Model: "AFTKA");
        return new FireTvDeviceProbe(
            Client(),
            _ => Task.FromResult(advertisements),
            _ => Task.FromResult(probeScan()),
            (_, _) => Task.FromResult(result));
    }

    private static ServiceInstance Advertised(IPAddress address, params string[] text)
    {
        var attributes = text.Select(entry => entry.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty);
        return new ServiceInstance("tv", address, 8009, attributes, text);
    }

    private static Task<IReadOnlyList<T>> Empty<T>() => Task.FromResult<IReadOnlyList<T>>([]);

    private static AdbProbeClient Client()
    {
        // Not disposed here: the identity signs with this key for as long as the client lives.
        var rsa = RSA.Create(2048);
        return new AdbProbeClient(new FixedIdentityProvider(new AdbIdentity(rsa, "flint@test")));
    }

    private sealed class FixedIdentityProvider(AdbIdentity identity) : IAdbIdentityProvider
    {
        public AdbIdentity GetIdentity() => identity;
    }
}
