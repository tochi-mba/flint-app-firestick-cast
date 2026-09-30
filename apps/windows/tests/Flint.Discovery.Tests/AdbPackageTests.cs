using System.Net;
using System.Security.Cryptography;
using Flint.Core;
using Shouldly;

namespace Flint.Discovery.Tests;

/// <summary>What the television's package manager prints is read exactly, and nothing is invented.</summary>
public sealed class AdbPackageInventoryTests
{
    [Fact]
    public void ParseListing_ReadsPackageLinesOnly()
    {
        const string output = "package:com.rextechnologies.flint.receiver.debug\r\n"
            + "package:com.rextechnologies.flint.mobile\n"
            + "WARNING: something the shell printed\n"
            + "package:\n";

        var packages = AdbPackageInventory.ParseListing(output);

        packages.ShouldBe(["com.rextechnologies.flint.receiver.debug", "com.rextechnologies.flint.mobile"], ignoreOrder: true);
    }

    [Fact]
    public void ParseDump_ReadsTheFirstVersionLinesAndStopsAtTheSpace()
    {
        const string dump = "Packages:\n"
            + "  Package [com.rextechnologies.flint.receiver.debug] (1a2b3c):\n"
            + "    versionCode=1118 minSdk=25 targetSdk=35\n"
            + "    versionName=0.1.0-debug\n"
            + "    versionCode=7 minSdk=25 targetSdk=35\n";

        var installed = AdbPackageInventory.ParseDump("com.rextechnologies.flint.receiver.debug", dump);

        installed.PackageName.ShouldBe("com.rextechnologies.flint.receiver.debug");
        installed.VersionCode.ShouldBe(1118);
        installed.VersionName.ShouldBe("0.1.0-debug");
    }

    [Theory]
    [InlineData("versionName=null\nversionCode=abc")]
    [InlineData("versionName=\n")]
    [InlineData("nothing useful")]
    public void ParseDump_LeavesUnreadableVersionsUnknown(string dump)
    {
        var installed = AdbPackageInventory.ParseDump("p", dump);

        installed.VersionName.ShouldBeNull();
        installed.VersionCode.ShouldBeNull();
    }

    [Fact]
    public void ParseDump_RejectsABlankPackageName()
    {
        Should.Throw<ArgumentException>(() => AdbPackageInventory.ParseDump(" ", "versionCode=1"));
    }

    [Theory]
    [InlineData("Success", true, "Success")]
    [InlineData("  Success\n", true, "Success")]
    [InlineData("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]", false, "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]")]
    [InlineData("", false, "")]
    public void InstallOutcome_ReadsSuccessOnlyFromTheFirstWord(string output, bool succeeded, string relayed)
    {
        var outcome = AdbInstallOutcome.FromOutput(output);

        outcome.Succeeded.ShouldBe(succeeded);
        outcome.Output.ShouldBe(relayed);
    }

    [Fact]
    public void InstallOutcome_BoundsWhatItRelays()
    {
        var outcome = AdbInstallOutcome.FromOutput("Failure " + new string('x', 2000));

        outcome.Output.Length.ShouldBe(AdbInstallOutcome.MaximumOutputLength);
    }
}

/// <summary>
/// The install and inventory calls against a fake device, so the stream protocol they rely on —
/// one OKAY per WRTE, chunks no larger than the peer announced — is exercised for real.
/// </summary>
public sealed class AdbProbeClientPackageTests
{
    private static readonly IPAddress Loopback = IPAddress.Loopback;

    [Fact]
    public async Task FindInstalledPackage_ReportsTheFirstCandidateTheTvLists()
    {
        await using var device = new FakeAdbDevice((service, _) => service switch
        {
            "shell:pm list packages com.rextechnologies.flint" =>
                "package:com.rextechnologies.flint.receiver\npackage:com.rextechnologies.flint.receiver.debug\n",
            "shell:dumpsys package com.rextechnologies.flint.receiver.debug" =>
                "    versionCode=1100 minSdk=25\n    versionName=0.1.0-debug\n",
            _ => throw new InvalidOperationException($"Unexpected service {service}"),
        });

        var installed = await Client().FindInstalledPackageAsync(
            Loopback,
            device.Port,
            [BundledReceiver.DebugPackage, BundledReceiver.ReleasePackage],
            TestContext.Current.CancellationToken);

        installed.ShouldNotBeNull();
        installed.PackageName.ShouldBe(BundledReceiver.DebugPackage);
        installed.VersionCode.ShouldBe(1100);
        installed.VersionName.ShouldBe("0.1.0-debug");
    }

    [Fact]
    public async Task FindInstalledPackage_NothingListed_IsNullAndAsksNothingMore()
    {
        await using var device = new FakeAdbDevice((_, _) => "package:com.rextechnologies.flint.mobile\n");

        var installed = await Client().FindInstalledPackageAsync(
            Loopback,
            device.Port,
            [BundledReceiver.DebugPackage],
            TestContext.Current.CancellationToken);

        installed.ShouldBeNull();
        device.Requests.ShouldBe(["shell:pm list packages com.rextechnologies.flint"]);
    }

    [Fact]
    public async Task FindInstalledPackage_RejectsCandidatesThatAreNotPackageNames()
    {
        await Should.ThrowAsync<ArgumentException>(() => Client().FindInstalledPackageAsync(
            Loopback,
            5555,
            ["not a package; rm -rf"],
            TestContext.Current.CancellationToken));
        await Should.ThrowAsync<ArgumentException>(() => Client().FindInstalledPackageAsync(
            Loopback,
            5555,
            [],
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstallPackage_StreamsEveryByteInChunksThePeerAcceptsAndReadsTheVerdict()
    {
        var apk = new byte[150_001];
        Random.Shared.NextBytes(apk);
        byte[]? delivered = null;
        await using var device = new FakeAdbDevice(
            (service, payload) =>
            {
                delivered = payload.ToArray();
                return service == $"exec:cmd package install -r -S {apk.Length}" ? "Success\n" : "Failure [unexpected service]";
            },
            maxData: 4096);
        // Not Progress<T>: that posts every report to the thread pool, where concurrent Adds on a
        // List corrupted it and took the whole test host down. The client reports synchronously.
        var progress = new LastFraction();

        var outcome = await Client().InstallPackageAsync(
            Loopback,
            device.Port,
            apk,
            progress,
            TestContext.Current.CancellationToken);

        outcome.Succeeded.ShouldBeTrue();
        outcome.Output.ShouldBe("Success");
        delivered.ShouldNotBeNull();
        delivered.ShouldBe(apk);
        progress.Value.ShouldBe(1.0, "every byte was acknowledged");
    }

    [Fact]
    public async Task InstallPackage_RelaysThePackageManagersRefusal()
    {
        await using var device = new FakeAdbDevice((_, _) => "Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE]\n");

        var outcome = await Client().InstallPackageAsync(
            Loopback,
            device.Port,
            new byte[10],
            progress: null,
            TestContext.Current.CancellationToken);

        outcome.Succeeded.ShouldBeFalse();
        outcome.Output.ShouldContain("INSTALL_FAILED_UPDATE_INCOMPATIBLE");
    }

    [Fact]
    public async Task InstallPackage_RefusesAnEmptyPackage()
    {
        await Should.ThrowAsync<ArgumentException>(() => Client().InstallPackageAsync(
            Loopback,
            5555,
            ReadOnlyMemory<byte>.Empty,
            progress: null,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LaunchActivity_StillRunsOverTheSharedStreamPath()
    {
        await using var device = new FakeAdbDevice((_, _) => "Starting: Intent { cmp=x/y }\n");

        await Client().LaunchActivityAsync(
            Loopback,
            device.Port,
            BundledReceiver.DebugPackage,
            BundledReceiver.MainActivity,
            TestContext.Current.CancellationToken);

        device.Requests.ShouldBe([$"shell:am start -n {BundledReceiver.DebugPackage}/{BundledReceiver.MainActivity}"]);
    }

    [Fact]
    public async Task LaunchActivity_RelaysAnActivityManagerError()
    {
        await using var device = new FakeAdbDevice((_, _) => "Error: Activity class does not exist.\n");

        var failure = await Should.ThrowAsync<AdbProtocolException>(() => Client().LaunchActivityAsync(
            Loopback,
            device.Port,
            BundledReceiver.DebugPackage,
            BundledReceiver.MainActivity,
            TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("does not exist");
    }

    [Fact]
    public async Task ProbePort_StillReadsBuildPropertiesOverTheSharedStreamPath()
    {
        await using var device = new FakeAdbDevice((_, _) =>
            "[ro.build.version.sdk]: [30]\n[ro.build.version.release]: [11]\n[ro.product.model]: [AFTKA]\n");

        var result = await Client().ProbePortAsync(Loopback, device.Port, TestContext.Current.CancellationToken);

        result.State.ShouldBe(AdbConnectionState.Connected);
        result.AndroidApiLevel.ShouldBe(30);
        result.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public async Task NothingListening_IsReportedAsUnreachableRatherThanUnauthorised()
    {
        using var closed = new System.Net.Sockets.TcpListener(Loopback, 0);
        closed.Start();
        var port = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();

        var failure = await Should.ThrowAsync<IOException>(() => Client().FindInstalledPackageAsync(
            Loopback,
            port,
            [BundledReceiver.DebugPackage],
            TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("did not accept");
    }

    private static AdbProbeClient Client()
    {
        // Not disposed here: the identity signs with this key for as long as the client lives.
        var rsa = RSA.Create(2048);
        return new AdbProbeClient(new FixedIdentityProvider(new AdbIdentity(rsa, "flint@test")));
    }

    /// <summary>Keeps the latest fraction. Reports arrive synchronously, on the caller's thread.</summary>
    private sealed class LastFraction : IProgress<double>
    {
        public double Value { get; private set; }

        public void Report(double value) => Value = value;
    }

    private sealed class FixedIdentityProvider(AdbIdentity identity) : IAdbIdentityProvider
    {
        public AdbIdentity GetIdentity() => identity;
    }
}
