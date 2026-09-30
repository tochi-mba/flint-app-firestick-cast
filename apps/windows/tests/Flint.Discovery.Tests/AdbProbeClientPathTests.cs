using System.Net;
using System.Security.Cryptography;
using System.Text;
using Flint.Core;
using Shouldly;

namespace Flint.Discovery.Tests;

/// <summary>
/// Every way a television can refuse, stall or misbehave during the ADB exchange, and what Flint
/// reports for each. A probe must answer "not authorised" or "not ADB" — never hang and never guess.
/// </summary>
public sealed class AdbProbeClientPathTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task ATvThatAsksForTls_IsReportedUnauthorisedAndRefusesPackageWork()
    {
        await using var probePeer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.StartTls, 1, 0, []), token);
        });

        var probed = await Client().ProbePortAsync(IPAddress.Loopback, probePeer.Port, Token);

        probed.State.ShouldBe(AdbConnectionState.Unauthorized);

        await using var installPeer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.StartTls, 1, 0, []), token);
        });
        var failure = await Should.ThrowAsync<IOException>(() => Client().FindInstalledPackageAsync(
            IPAddress.Loopback,
            installPeer.Port,
            [BundledReceiver.DebugPackage],
            Token));
        failure.Message.ShouldBe("The Fire TV did not authorize Flint to read what is installed.");
    }

    [Fact]
    public async Task AnIdentityThatCannotBeRead_IsUnauthorisedRatherThanACrash()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Challenge(), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });
        var client = new AdbProbeClient(new ThrowingIdentityProvider(new CryptographicException("key store unreadable")));

        var result = await client.ProbePortAsync(IPAddress.Loopback, peer.Port, Token);

        result.State.ShouldBe(AdbConnectionState.Unauthorized);
    }

    [Fact]
    public async Task APromptNobodyAnswers_IsUnauthorisedOnceThePromptWindowCloses()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ChallengeTwiceAsync(stream, token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var result = await Client(promptTimeout: Short).ProbePortAsync(IPAddress.Loopback, peer.Port, Token);

        result.State.ShouldBe(AdbConnectionState.Unauthorized);
        result.Port.ShouldBe(peer.Port);
    }

    [Fact]
    public async Task APromptThatIsDeclined_IsUnauthorised()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ChallengeTwiceAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Challenge(), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var result = await Client().ProbePortAsync(IPAddress.Loopback, peer.Port, Token);

        result.State.ShouldBe(AdbConnectionState.Unauthorized);
    }

    [Fact]
    public async Task ATvThatHangsUpDuringThePrompt_IsUnauthorised()
    {
        await using var peer = new ScriptedAdbPeer(ChallengeTwiceAsync);

        var result = await Client().ProbePortAsync(IPAddress.Loopback, peer.Port, Token);

        result.State.ShouldBe(AdbConnectionState.Unauthorized);
    }

    [Fact]
    public async Task AnAcceptedPrompt_ConnectsOnTheSameProbe()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ChallengeTwiceAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var result = await Client(handshakeTimeout: TimeSpan.FromSeconds(2)).ProbePortAsync(IPAddress.Loopback, peer.Port, Token);

        result.State.ShouldBe(AdbConnectionState.Connected);
        result.Banner.ShouldNotBeNull();
        result.Banner.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public async Task PropertiesThatNeverArrive_StillReportAConnectedTv()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var result = await Client(handshakeTimeout: Short).ProbePortAsync(IPAddress.Loopback, peer.Port, Token);

        result.State.ShouldBe(AdbConnectionState.Connected);
        result.AndroidApiLevel.ShouldBeNull();
    }

    [Fact]
    public async Task PropertiesThatAreNotAdb_StillReportAConnectedTv()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("this is not an adb header at all"), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var result = await Client().ProbePortAsync(IPAddress.Loopback, peer.Port, Token);

        result.State.ShouldBe(AdbConnectionState.Connected);
        result.Model.ShouldBeNull();
    }

    [Fact]
    public async Task ServiceOutputBeyondTheCap_IsRefusedRatherThanBuffered()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            var open = await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Okay, 77, open.Arg0, []), token);
            await ScriptedAdbPeer.WriteAsync(
                stream,
                new AdbMessage(AdbCommand.Write, 77, open.Arg0, new byte[AdbMessage.MaxPayloadLength]),
                token);
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Write, 77, open.Arg0, [1]), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var failure = await Should.ThrowAsync<AdbProtocolException>(() => Client().FindInstalledPackageAsync(
            IPAddress.Loopback,
            peer.Port,
            [BundledReceiver.DebugPackage],
            Token));

        failure.Message.ShouldContain("safety cap");
    }

    [Fact]
    public async Task AnUnexpectedMessageInAService_IsAProtocolError()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            var open = await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner() with { Arg1 = open.Arg0 }, token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var failure = await Should.ThrowAsync<AdbProtocolException>(() => Client().FindInstalledPackageAsync(
            IPAddress.Loopback,
            peer.Port,
            [BundledReceiver.DebugPackage],
            Token));

        failure.Message.ShouldContain("Unexpected ADB command Connect in the service stream");
    }

    [Fact]
    public async Task AServiceThatWritesBeforeItsOkay_IsStillRead()
    {
        // adbd may answer a short command with data straight away; the stream id comes from that.
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            var open = await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(
                stream,
                new AdbMessage(AdbCommand.Write, 77, open.Arg0, Encoding.UTF8.GetBytes("package:com.rextechnologies.flint.mobile\n")),
                token);
            (await ScriptedAdbPeer.ReadAsync(stream, token)).Command.ShouldBe(AdbCommand.Okay);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Close, 77, open.Arg0, []), token);
            var close = await ScriptedAdbPeer.ReadAsync(stream, token);
            close.Command.ShouldBe(AdbCommand.Close);
            close.Arg1.ShouldBe(77u, "the host closes the stream id it learned from the write");
        });

        var installed = await Client().FindInstalledPackageAsync(
            IPAddress.Loopback,
            peer.Port,
            [BundledReceiver.DebugPackage],
            Token);
        await peer.Completion;

        installed.ShouldBeNull();
    }

    [Fact]
    public async Task ADeviceThatAnnouncesNoMessageSize_GetsTheSmallestSafeChunks()
    {
        var apk = new byte[10_000];
        Random.Shared.NextBytes(apk);
        byte[]? delivered = null;
        await using var device = new FakeAdbDevice(
            (_, payload) =>
            {
                delivered = payload.ToArray();
                return "Success";
            },
            maxData: 4096,
            announcedMaxData: 0);

        var outcome = await Client().InstallPackageAsync(IPAddress.Loopback, device.Port, apk, progress: null, Token);

        outcome.Succeeded.ShouldBeTrue();
        delivered.ShouldBe(apk);
    }

    [Fact]
    public async Task AnAddressThatCannotBeConnectedTo_IsReportedAsUnreachable()
    {
        // Connecting to the unspecified address fails at once with a socket error, rather than
        // waiting out the connect timeout the way a silent host does.
        var failure = await Should.ThrowAsync<IOException>(() => Client().FindInstalledPackageAsync(
            IPAddress.Any,
            5555,
            [BundledReceiver.DebugPackage],
            Token));

        failure.Message.ShouldStartWith("The Fire TV did not accept the ADB connection");
    }

    [Theory]
    [InlineData("com.example.app", "bad activity; reboot")]
    [InlineData("bad package; reboot", ".Main")]
    public async Task ComponentNamesWithShellCharacters_AreRefusedBeforeConnecting(string package, string activity)
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            Client().LaunchActivityAsync(IPAddress.Loopback, 5555, package, activity, Token));
    }

    [Fact]
    public async Task AnEmptyCandidateName_IsRefusedBeforeConnecting()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            Client().FindInstalledPackageAsync(IPAddress.Loopback, 5555, [string.Empty], Token));
    }

    [Fact]
    public async Task CancellingWhileThePropertiesAreAwaited_IsCancelledRatherThanReportedConnected()
    {
        // The property read has its own timeout, after which a connected TV is still reported. A
        // cancellation from the caller is not that timeout and must not be dressed up as a result.
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(() => Client(handshakeTimeout: TimeSpan.FromSeconds(10))
            .ProbePortAsync(IPAddress.Loopback, peer.Port, cancel.Token));
    }

    [Fact]
    public async Task TheTvsLateCloseOfTheListing_IsNotTakenForTheVersionLookupClosing()
    {
        // The exchange a Fire TV Stick 4K Max (AFTMM) produced: adbd answers the host's CLSE of the
        // listing with one of its own, which arrives after the dumpsys OPEN has gone out. Under one
        // reused stream id that CLSE ended the lookup before it began, so every installed receiver
        // read as "version unknown" and Windows could never offer an update.
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);

            var listing = await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Okay, 31, listing.Arg0, []), token);
            await ScriptedAdbPeer.WriteAsync(
                stream,
                new AdbMessage(AdbCommand.Write, 31, listing.Arg0, Encoding.UTF8.GetBytes("package:com.rextechnologies.flint.receiver.debug\n")),
                token);
            (await ScriptedAdbPeer.ReadAsync(stream, token)).Command.ShouldBe(AdbCommand.Okay);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Close, 31, listing.Arg0, []), token);
            (await ScriptedAdbPeer.ReadAsync(stream, token)).Command.ShouldBe(AdbCommand.Close);

            var lookup = await ScriptedAdbPeer.ReadAsync(stream, token);
            lookup.Command.ShouldBe(AdbCommand.Open);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Close, 31, listing.Arg0, []), token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Okay, 32, lookup.Arg0, []), token);
            await ScriptedAdbPeer.WriteAsync(
                stream,
                new AdbMessage(AdbCommand.Write, 32, lookup.Arg0, Encoding.UTF8.GetBytes("    versionCode=1 minSdk=25 targetSdk=36\n    versionName=0.1.0-debug\n")),
                token);
            (await ScriptedAdbPeer.ReadAsync(stream, token)).Command.ShouldBe(AdbCommand.Okay);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Close, 32, lookup.Arg0, []), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var installed = await Client().FindInstalledPackageAsync(
            IPAddress.Loopback,
            peer.Port,
            [BundledReceiver.ReleasePackage, BundledReceiver.DebugPackage],
            Token);

        installed.ShouldNotBeNull();
        installed.PackageName.ShouldBe(BundledReceiver.DebugPackage);
        installed.VersionCode.ShouldBe(1);
        installed.VersionName.ShouldBe("0.1.0-debug");
    }

    [Fact]
    public async Task AServiceThatGoesQuiet_EndsAsACancellationTheCallerDidNotAskFor()
    {
        // The Cast page tells "the TV stopped answering" from "the person cancelled" by exactly
        // this: a cancellation while the caller's own token was never cancelled.
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(), token);
            var open = await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Okay, 77, open.Arg0, []), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        await Should.ThrowAsync<OperationCanceledException>(() => Client(handshakeTimeout: Short)
            .FindInstalledPackageAsync(IPAddress.Loopback, peer.Port, [BundledReceiver.DebugPackage], Token));

        Token.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public async Task AChunkTheTvNeverAcknowledges_IsNotCountedAsCopiedAndFailsTheInstall()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(maxData: 4), token);
            var open = await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Okay, 77, open.Arg0, []), token);
            (await ScriptedAdbPeer.ReadAsync(stream, token)).Command.ShouldBe(AdbCommand.Write);
            // Gone before acknowledging it, as when the package manager dies or the TV sleeps.
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Close, 77, open.Arg0, []), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });
        var progress = new RecordingProgress();

        var failure = await Should.ThrowAsync<IOException>(
            () => Client().InstallPackageAsync(IPAddress.Loopback, peer.Port, new byte[10], progress, Token));

        failure.Message.ShouldBe("The TV closed the install after taking 0 of 10 bytes.");
        progress.Values.ShouldBeEmpty("the one chunk sent was never acknowledged");
    }

    [Fact]
    public async Task ATvThatRefusesBeforeTakingEverything_KeepsItsReason()
    {
        await using var peer = new ScriptedAdbPeer(async (stream, token) =>
        {
            await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Banner(maxData: 4), token);
            var open = await ScriptedAdbPeer.ReadAsync(stream, token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Okay, 77, open.Arg0, []), token);
            (await ScriptedAdbPeer.ReadAsync(stream, token)).Command.ShouldBe(AdbCommand.Write);
            // The package manager judged the declared size and answered without reading on.
            await ScriptedAdbPeer.WriteAsync(
                stream,
                new AdbMessage(AdbCommand.Write, 77, open.Arg0, Encoding.UTF8.GetBytes("Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]\n")),
                token);
            await ScriptedAdbPeer.WriteAsync(stream, new AdbMessage(AdbCommand.Close, 77, open.Arg0, []), token);
            await ScriptedAdbPeer.StaySilentAsync(stream, token);
        });

        var outcome = await Client().InstallPackageAsync(IPAddress.Loopback, peer.Port, new byte[10], progress: null, Token);

        outcome.Succeeded.ShouldBeFalse();
        outcome.Output.ShouldContain("INSTALL_FAILED_INSUFFICIENT_STORAGE");
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The first-time exchange up to the television's prompt: challenge, signature, challenge, key.</summary>
    private static async Task ChallengeTwiceAsync(System.Net.Sockets.NetworkStream stream, CancellationToken token)
    {
        (await ScriptedAdbPeer.ReadAsync(stream, token)).Command.ShouldBe(AdbCommand.Connect);
        await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Challenge(), token);
        (await ScriptedAdbPeer.ReadAsync(stream, token)).Arg0.ShouldBe(AdbAuthentication.SignatureType);
        await ScriptedAdbPeer.WriteAsync(stream, ScriptedAdbPeer.Challenge(), token);
        (await ScriptedAdbPeer.ReadAsync(stream, token)).Arg0.ShouldBe(AdbAuthentication.PublicKeyType);
    }

    private static AdbProbeClient Client(TimeSpan? handshakeTimeout = null, TimeSpan? promptTimeout = null)
    {
        // Not disposed here: the identity signs with this key for as long as the client lives.
        var rsa = RSA.Create(2048);
        return new AdbProbeClient(
            new FixedIdentityProvider(new AdbIdentity(rsa, "flint@test")),
            handshakeTimeout,
            promptTimeout);
    }

    private sealed class FixedIdentityProvider(AdbIdentity identity) : IAdbIdentityProvider
    {
        public AdbIdentity GetIdentity() => identity;
    }

    private sealed class ThrowingIdentityProvider(Exception failure) : IAdbIdentityProvider
    {
        public AdbIdentity GetIdentity() => throw failure;
    }
}
