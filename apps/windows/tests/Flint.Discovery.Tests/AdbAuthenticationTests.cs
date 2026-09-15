using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Flint.Core;
using Shouldly;

namespace Flint.Discovery.Tests;

public sealed class AdbAuthenticationTests
{
    [Fact]
    public void SignatureMessage_SignsTheAlreadyHashedTwentyByteChallenge()
    {
        using var rsa = RSA.Create(2048);
        var identity = new AdbIdentity(rsa, "flint@test");
        var token = Enumerable.Range(0, AdbAuthentication.TokenLength).Select(value => (byte)value).ToArray();

        var message = AdbAuthentication.SignatureMessage(identity, token);

        message.Command.ShouldBe(AdbCommand.Auth);
        message.Arg0.ShouldBe(AdbAuthentication.SignatureType);
        rsa.VerifyHash(token, message.Payload, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1)
            .ShouldBeTrue();
    }

    [Fact]
    public void SignatureMessage_RejectsAnythingButAnAdbToken()
    {
        using var rsa = RSA.Create(2048);
        var identity = new AdbIdentity(rsa, "flint@test");

        Should.Throw<CryptographicException>(
            () => AdbAuthentication.SignatureMessage(identity, new byte[19]));
    }

    [Fact]
    public void PublicKeyMessage_UsesAndroidsFixedMontgomeryWireShape()
    {
        using var rsa = RSA.Create(2048);
        var identity = new AdbIdentity(rsa, "flint@test");

        var message = AdbAuthentication.PublicKeyMessage(identity);
        var line = Encoding.ASCII.GetString(message.Payload);
        var encoded = line.TrimEnd('\0').Split(' ', 2);
        var binary = Convert.FromBase64String(encoded[0]);

        message.Command.ShouldBe(AdbCommand.Auth);
        message.Arg0.ShouldBe(AdbAuthentication.PublicKeyType);
        encoded[1].ShouldBe("flint@test");
        binary.Length.ShouldBe(AdbAuthentication.PublicKeyBinaryLength);
        BinaryPrimitives.ReadUInt32LittleEndian(binary).ShouldBe(64u);

        var n0Inverse = BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(4));
        var lowModulusWord = BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(8));
        unchecked(lowModulusWord * n0Inverse).ShouldBe(uint.MaxValue);

        var expectedModulus = rsa.ExportParameters(false).Modulus!;
        var wireModulus = binary.AsSpan(8, AdbAuthentication.ModulusBytes).ToArray();
        Array.Reverse(wireModulus);
        wireModulus.ShouldBe(expectedModulus);
        BinaryPrimitives.ReadUInt32LittleEndian(binary.AsSpan(binary.Length - 4)).ShouldBe(65_537u);
    }

    [Fact]
    public void Identity_RequiresRsa2048AndASafePromptLabel()
    {
        using var weak = RSA.Create(1024);
        using var strong = RSA.Create(2048);

        Should.Throw<CryptographicException>(() => new AdbIdentity(weak, "flint@test"));
        Should.Throw<ArgumentException>(() => new AdbIdentity(strong, "bad\nlabel"));
    }

    [Fact]
    public void FileIdentityProvider_PersistsOneStableHostKey()
    {
        var directory = Path.Combine(Path.GetTempPath(), "flint-adb-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "adbkey.pem");
        try
        {
            var first = new FileAdbIdentityProvider(path).GetIdentity();
            var second = new FileAdbIdentityProvider(path).GetIdentity();

            File.Exists(path).ShouldBeTrue();
            first.Rsa.ExportSubjectPublicKeyInfo().ShouldBe(second.Rsa.ExportSubjectPublicKeyInfo());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

public sealed class AdbBuildPropertiesTests
{
    [Fact]
    public void Parse_ReadsApiAndroidReleaseAndModelWithoutRelabellingAndroidAsFireOs()
    {
        const string output = "[ro.build.version.sdk]: [30]\r\n"
            + "[ro.build.version.release]: [11]\n"
            + "[ro.product.model]: [AFTKA]\n";

        var properties = AdbBuildProperties.Parse(output);

        properties.AndroidApiLevel.ShouldBe(30);
        properties.AndroidRelease.ShouldBe("11");
        properties.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public void Parse_MalformedOrMissingValuesStayUnknown()
    {
        var properties = AdbBuildProperties.Parse(
            "garbage\n[ro.build.version.sdk]: [not-a-number]\n[ro.product.model]: []\n");

        properties.AndroidApiLevel.ShouldBeNull();
        properties.AndroidRelease.ShouldBeNull();
        properties.Model.ShouldBeNull();
    }

    [Theory]
    [InlineData("AFTCA002")]
    [InlineData("aftcl001")]
    public void Resolve_OfficialVegaBuildModel_IsConclusiveEvenWhenAdbIsAbsent(string model)
    {
        FireTvPlatformResolver.Resolve(AdbProbeResult.NotFound, null, model)
            .ShouldBe(FireTvPlatform.Vega);
    }

    [Fact]
    public void Resolve_UnknownSilentModel_RemainsAmbiguous()
    {
        FireTvPlatformResolver.Resolve(AdbProbeResult.NotFound, null, "AFT-UNKNOWN")
            .ShouldBe(FireTvPlatform.Unknown);
    }
}

public sealed class AdbProbeClientIntegrationTests
{
    [Fact]
    public async Task FireTvProbe_ExplicitAddress_ProducesAManualDeviceWithoutMulticast()
    {
        using var rsa = RSA.Create(2048);
        var client = ClientFor(rsa);
        using var timeout = LinkedTimeout();
        await using var server = new ScriptedAdbServer(async (stream, cancellationToken) =>
        {
            await ReadAsync(stream, cancellationToken);
            await WriteAsync(stream, ConnectBanner(), cancellationToken);
            await ServePropertiesAsync(stream, cancellationToken);
        });

        var device = await new FireTvDeviceProbe(client).ProbeAddressAsync(
            IPAddress.Loopback,
            server.Port,
            timeout.Token);
        await server.Completion;

        device.Address.ShouldBe(IPAddress.Loopback);
        device.Source.ShouldBe(DiscoverySource.Manual);
        device.Model.ShouldBe("AFTKA");
        device.AndroidApiLevel.ShouldBe(30);
        device.AndroidRelease.ShouldBe("11");
        device.Platform.ShouldBe(FireTvPlatform.FireOs8);
        device.AdbState.ShouldBe(AdbConnectionState.Connected);
    }

    [Fact]
    public async Task ProbePortAsync_AuthorizedPeer_ReadsOnlyBuildProperties()
    {
        using var rsa = RSA.Create(2048);
        var client = ClientFor(rsa);
        using var timeout = LinkedTimeout();
        await using var server = new ScriptedAdbServer(async (stream, cancellationToken) =>
        {
            (await ReadAsync(stream, cancellationToken)).Command.ShouldBe(AdbCommand.Connect);
            await WriteAsync(stream, ConnectBanner(), cancellationToken);
            await ServePropertiesAsync(stream, cancellationToken);
        });

        var result = await client.ProbePortAsync(
            IPAddress.Loopback,
            server.Port,
            timeout.Token);
        await server.Completion;

        result.State.ShouldBe(AdbConnectionState.Connected);
        result.AndroidApiLevel.ShouldBe(30);
        result.AndroidRelease.ShouldBe("11");
        result.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public async Task ProbePortAsync_KnownKey_SignsChallengeThenReadsProperties()
    {
        using var rsa = RSA.Create(2048);
        var client = ClientFor(rsa);
        using var timeout = LinkedTimeout();
        var token = RandomNumberGenerator.GetBytes(AdbAuthentication.TokenLength);
        await using var server = new ScriptedAdbServer(async (stream, cancellationToken) =>
        {
            await ReadAsync(stream, cancellationToken);
            await WriteAsync(stream, AuthToken(token), cancellationToken);
            var signature = await ReadAsync(stream, cancellationToken);
            rsa.VerifyHash(token, signature.Payload, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1)
                .ShouldBeTrue();
            await WriteAsync(stream, ConnectBanner(), cancellationToken);
            await ServePropertiesAsync(stream, cancellationToken);
        });

        var result = await client.ProbePortAsync(IPAddress.Loopback, server.Port, timeout.Token);
        await server.Completion;

        result.State.ShouldBe(AdbConnectionState.Connected);
        result.AndroidApiLevel.ShouldBe(30);
    }

    [Fact]
    public async Task LaunchActivityAsync_AuthorizedPeer_OpensTheRequestedReceiverActivity()
    {
        using var rsa = RSA.Create(2048);
        var client = ClientFor(rsa);
        using var timeout = LinkedTimeout();
        await using var server = new ScriptedAdbServer(async (stream, cancellationToken) =>
        {
            await ReadAsync(stream, cancellationToken);
            await WriteAsync(stream, ConnectBanner(), cancellationToken);
            await ServeShellAsync(
                stream,
                "am start -n com.rextechnologies.flint.receiver.debug/com.rextechnologies.flint.receiver.ReceiverActivity",
                "Starting: Intent { cmp=com.rextechnologies.flint.receiver.debug/.ReceiverActivity }\n",
                cancellationToken);
        });

        await client.LaunchActivityAsync(
            IPAddress.Loopback,
            server.Port,
            "com.rextechnologies.flint.receiver.debug",
            "com.rextechnologies.flint.receiver.ReceiverActivity",
            timeout.Token);
        await server.Completion;
    }

    [Fact]
    public async Task ProbePortAsync_UnknownKey_OffersPublicKeyAndLeavesConsentToTheTv()
    {
        using var rsa = RSA.Create(2048);
        var client = ClientFor(rsa);
        using var timeout = LinkedTimeout();
        var token = RandomNumberGenerator.GetBytes(AdbAuthentication.TokenLength);
        AdbMessage? offeredKey = null;
        await using var server = new ScriptedAdbServer(async (stream, cancellationToken) =>
        {
            await ReadAsync(stream, cancellationToken);
            await WriteAsync(stream, AuthToken(token), cancellationToken);
            (await ReadAsync(stream, cancellationToken)).Arg0.ShouldBe(AdbAuthentication.SignatureType);
            await WriteAsync(stream, AuthToken(token), cancellationToken);
            offeredKey = await ReadAsync(stream, cancellationToken);
        });

        var result = await client.ProbePortAsync(IPAddress.Loopback, server.Port, timeout.Token);
        await server.Completion;

        result.State.ShouldBe(AdbConnectionState.Unauthorized);
        offeredKey.ShouldNotBeNull();
        offeredKey.Arg0.ShouldBe(AdbAuthentication.PublicKeyType);
        Encoding.ASCII.GetString(offeredKey.Payload).ShouldEndWith(" flint@test\0");
    }

    [Fact]
    public async Task ProbePortAsync_FirstTimeConsent_CanCompleteWithoutASecondProbe()
    {
        using var rsa = RSA.Create(2048);
        var client = ClientFor(rsa);
        using var timeout = LinkedTimeout();
        var token = RandomNumberGenerator.GetBytes(AdbAuthentication.TokenLength);
        await using var server = new ScriptedAdbServer(async (stream, cancellationToken) =>
        {
            await ReadAsync(stream, cancellationToken);
            await WriteAsync(stream, AuthToken(token), cancellationToken);
            (await ReadAsync(stream, cancellationToken)).Arg0.ShouldBe(AdbAuthentication.SignatureType);
            await WriteAsync(stream, AuthToken(token), cancellationToken);
            (await ReadAsync(stream, cancellationToken)).Arg0.ShouldBe(AdbAuthentication.PublicKeyType);
            await WriteAsync(stream, ConnectBanner(), cancellationToken);
            await ServePropertiesAsync(stream, cancellationToken);
        });

        var result = await client.ProbePortAsync(IPAddress.Loopback, server.Port, timeout.Token);
        await server.Completion;

        result.State.ShouldBe(AdbConnectionState.Connected);
        result.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public async Task ProbePortAsync_NonAdbService_IsRejected()
    {
        using var rsa = RSA.Create(2048);
        var client = ClientFor(rsa);
        using var timeout = LinkedTimeout();
        await using var server = new ScriptedAdbServer(async (stream, cancellationToken) =>
        {
            await ReadAsync(stream, cancellationToken);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("not adb at all"), cancellationToken);
        });

        var result = await client.ProbePortAsync(IPAddress.Loopback, server.Port, timeout.Token);
        await server.Completion;

        result.State.ShouldBe(AdbConnectionState.Refused);
    }

    private static AdbProbeClient ClientFor(RSA rsa) =>
        new(new FixedIdentityProvider(new AdbIdentity(rsa, "flint@test")));

    private static CancellationTokenSource LinkedTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return timeout;
    }

    private static AdbMessage AuthToken(byte[] token) =>
        new(AdbCommand.Auth, AdbAuthentication.TokenType, 0, token);

    private static AdbMessage ConnectBanner() => new(
        AdbCommand.Connect,
        AdbMessage.ProtocolVersion,
        AdbMessage.MaxPayloadAdvertised,
        Encoding.UTF8.GetBytes("device::ro.product.model=AFTKA;features=cmd\0"));

    private static async Task ServePropertiesAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var open = await ReadAsync(stream, cancellationToken);
        open.Command.ShouldBe(AdbCommand.Open);
        Encoding.UTF8.GetString(open.Payload).ShouldBe("shell:getprop\0");
        const uint remoteId = 77;
        await WriteAsync(
            stream,
            new AdbMessage(AdbCommand.Okay, remoteId, open.Arg0, []),
            cancellationToken);
        var properties = Encoding.UTF8.GetBytes(
            "[ro.build.version.sdk]: [30]\n"
            + "[ro.build.version.release]: [11]\n"
            + "[ro.product.model]: [AFTKA]\n");
        await WriteAsync(
            stream,
            new AdbMessage(AdbCommand.Write, remoteId, open.Arg0, properties),
            cancellationToken);
        (await ReadAsync(stream, cancellationToken)).Command.ShouldBe(AdbCommand.Okay);
        await WriteAsync(
            stream,
            new AdbMessage(AdbCommand.Close, remoteId, open.Arg0, []),
            cancellationToken);
        (await ReadAsync(stream, cancellationToken)).Command.ShouldBe(AdbCommand.Close);
    }

    private static async Task ServeShellAsync(
        NetworkStream stream,
        string expectedCommand,
        string output,
        CancellationToken cancellationToken)
    {
        var open = await ReadAsync(stream, cancellationToken);
        open.Command.ShouldBe(AdbCommand.Open);
        Encoding.UTF8.GetString(open.Payload).ShouldBe($"shell:{expectedCommand}\0");
        const uint remoteId = 77;
        await WriteAsync(stream, new AdbMessage(AdbCommand.Okay, remoteId, open.Arg0, []), cancellationToken);
        await WriteAsync(
            stream,
            new AdbMessage(AdbCommand.Write, remoteId, open.Arg0, Encoding.UTF8.GetBytes(output)),
            cancellationToken);
        (await ReadAsync(stream, cancellationToken)).Command.ShouldBe(AdbCommand.Okay);
        await WriteAsync(stream, new AdbMessage(AdbCommand.Close, remoteId, open.Arg0, []), cancellationToken);
        (await ReadAsync(stream, cancellationToken)).Command.ShouldBe(AdbCommand.Close);
    }

    private static async Task<AdbMessage> ReadAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var headerBytes = new byte[AdbMessage.HeaderLength];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken);
        var header = AdbMessage.ParseHeader(headerBytes);
        var payload = new byte[header.PayloadLength];
        if (payload.Length > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken);
        }

        return new AdbMessage(header.Command, header.Arg0, header.Arg1, payload);
    }

    private static async Task WriteAsync(
        NetworkStream stream,
        AdbMessage message,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(message.ToBytes(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private sealed class FixedIdentityProvider(AdbIdentity identity) : IAdbIdentityProvider
    {
        public AdbIdentity GetIdentity() => identity;
    }

    private sealed class ScriptedAdbServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _lifetime =
            CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        internal ScriptedAdbServer(Func<NetworkStream, CancellationToken, Task> script)
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _lifetime.CancelAfter(TimeSpan.FromSeconds(8));
            Completion = RunAsync(script, _lifetime.Token);
        }

        internal int Port { get; }

        internal Task Completion { get; }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _lifetime.Cancel();
            try
            {
                await Completion;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }

            _lifetime.Dispose();
        }

        private async Task RunAsync(
            Func<NetworkStream, CancellationToken, Task> script,
            CancellationToken cancellationToken)
        {
            using var peer = await _listener.AcceptTcpClientAsync(cancellationToken);
            await script(peer.GetStream(), cancellationToken);
        }
    }
}
