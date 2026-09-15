using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flint.Protocol;
using Flint.Session.Browser;
using Shouldly;
using Xunit.Sdk;

namespace Flint.Session.Tests;

public sealed class BrowserSessionTests
{
    [Fact]
    public async Task ConnectAsync_FirstUseTrustsAfterTlsAuthAndSendsBrowserCommandsOnlyOverTls()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new BrowserEndpoint(
            IPAddress.Loopback,
            ((IPEndPoint)listener.LocalEndpoint).Port,
            "receiver-test-a");
        var pairingCodeSeen = false;
        var server = ServeAsync(listener, certificate, async stream =>
        {
            var hello = await ReadFrameAsync(stream);
            hello.ProtocolVersion.ShouldBe(2);
            var hostHello = hello.Message.ShouldBeOfType<HelloMessage>();
            hostHello.MinimumVersion.ShouldBe(2);
            var negotiated = NegotiateBrowserProtocol(hostHello);
            await WriteFrameAsync(stream, negotiated, ReceiverHello());
            var auth = (await ReadFrameAsync(stream));
            auth.ProtocolVersion.ShouldBe(negotiated);
            auth.Message.ShouldBeOfType<AuthMessage>().Method.ShouldBe(AuthMethod.PairingCode);
            ((AuthMessage)auth.Message).Credential.ToArray().ShouldBe(System.Text.Encoding.ASCII.GetBytes("123456"));
            pairingCodeSeen = true;
            await WriteFrameAsync(stream, negotiated, AvailableCapability(endpoint.Port));

            var commandFrame = await ReadFrameAsync(stream);
            commandFrame.ProtocolVersion.ShouldBe(negotiated);
            var command = commandFrame.Message.ShouldBeOfType<BrowserCommandMessage>();
            command.ShouldBe(new BrowserCommandMessage(7, 1, BrowserCommandAction.Open, "https://example.test/"));
            await WriteFrameAsync(stream, negotiated, new ByeMessage(ByeReason.Normal, "done"));
        });
        var store = new InMemoryBrowserTrustStore();
        var prompt = new RecordingTrustPrompter(accepted: true);

        await using var session = await BrowserSession.ConnectAsync(
            endpoint,
            "123456",
            store,
            prompt,
            cancellationToken: TestContext.Current.CancellationToken);
        await session.SendCommandAsync(
            new BrowserCommandMessage(7, 1, BrowserCommandAction.Open, "https://example.test/"),
            TestContext.Current.CancellationToken);

        pairingCodeSeen.ShouldBeTrue();
        prompt.Seen.ShouldBe(BrowserFingerprint.FromCertificate(certificate).DisplayCode);
        var pinned = await store.FindAsync(endpoint.ReceiverIdentity, TestContext.Current.CancellationToken);
        pinned.ShouldNotBeNull();
        pinned.ReceiverIdentity.ShouldBe(endpoint.ReceiverIdentity);
        pinned.Fingerprint.ShouldBe(BrowserFingerprint.FromCertificate(certificate));
        await server;
    }

    [Fact]
    public async Task SecureSession_ReceivesLibraryRequestsAndSendsTheWindowsProfileSnapshot()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new BrowserEndpoint(
            IPAddress.Loopback,
            ((IPEndPoint)listener.LocalEndpoint).Port,
            "receiver-library-direction");
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedSnapshot = new TaskCompletionSource<BrowserLibraryStateMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ServeAsync(listener, certificate, async stream =>
        {
            var negotiated = await CompleteReceiverHelloAsync(stream);
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, negotiated, AvailableCapability(endpoint.Port));
            await releaseRequest.Task.WaitAsync(TestContext.Current.CancellationToken);
            await WriteFrameAsync(
                stream,
                negotiated,
                new BrowserLibraryCommandMessage(
                    9,
                    1,
                    BrowserLibraryAction.RequestSnapshot));
            receivedSnapshot.TrySetResult(
                (await ReadFrameAsync(stream)).Message.ShouldBeOfType<BrowserLibraryStateMessage>());
            await WriteFrameAsync(stream, negotiated, new ByeMessage(ByeReason.Normal, "done"));
        });

        await using var session = await BrowserSession.ConnectAsync(
            endpoint,
            "123456",
            new InMemoryBrowserTrustStore(),
            new RecordingTrustPrompter(accepted: true),
            cancellationToken: TestContext.Current.CancellationToken);
        var request = new TaskCompletionSource<BrowserLibraryCommandMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.LibraryRequestReceived += message => request.TrySetResult(message);
        releaseRequest.TrySetResult();

        var announced = await request.Task.WaitAsync(TestContext.Current.CancellationToken);
        announced.Action.ShouldBe(BrowserLibraryAction.RequestSnapshot);
        var snapshot = new BrowserLibraryStateMessage(
            Epoch: 9,
            Revision: 1,
            Bookmarks: ValueList<BrowserLibraryEntry>.From(
            [
                new BrowserLibraryEntry(
                    BrowserLibraryEntryKind.Bookmark,
                    FaviconId: 0,
                    LastVisitedMilliseconds: 10,
                    Url: "https://example.test/",
                    Title: "Example"),
            ]),
            History: ValueList<BrowserLibraryEntry>.Empty);
        await session.SendLibraryStateAsync(snapshot, TestContext.Current.CancellationToken);

        (await receivedSnapshot.Task.WaitAsync(TestContext.Current.CancellationToken)).ShouldBe(snapshot);
        await server;
    }

    [Fact]
    public async Task SecureSession_CarriesTvLibraryStateAndAllOrderedCockpitCommands()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new BrowserEndpoint(
            IPAddress.Loopback,
            ((IPEndPoint)listener.LocalEndpoint).Port,
            "receiver-cockpit-bidirectional");
        var releaseState = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new TaskCompletionSource<IReadOnlyList<WireMessage>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var tvSnapshot = new BrowserLibraryStateMessage(
            11,
            1,
            ValueList<BrowserLibraryEntry>.Empty,
            ValueList<BrowserLibraryEntry>.Empty);
        var server = ServeAsync(listener, certificate, async stream =>
        {
            var negotiated = await CompleteReceiverHelloAsync(stream);
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, negotiated, AvailableCapability(endpoint.Port));
            await releaseState.Task.WaitAsync(TestContext.Current.CancellationToken);
            await WriteFrameAsync(stream, negotiated, tvSnapshot);
            var messages = new List<WireMessage>();
            for (var index = 0; index < 3; index++)
            {
                messages.Add((await ReadFrameAsync(stream)).Message);
            }
            received.TrySetResult(messages);
            await WriteFrameAsync(stream, negotiated, new ByeMessage(ByeReason.Normal, "done"));
        });

        await using var session = await BrowserSession.ConnectAsync(
            endpoint,
            "123456",
            new InMemoryBrowserTrustStore(),
            new RecordingTrustPrompter(accepted: true),
            cancellationToken: TestContext.Current.CancellationToken);
        var state = new TaskCompletionSource<BrowserLibraryStateMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.LibraryReceived += message => state.TrySetResult(message);
        releaseState.TrySetResult();
        (await state.Task.WaitAsync(TestContext.Current.CancellationToken)).ShouldBe(tvSnapshot);

        await session.SendTabCommandAsync(
            new BrowserTabCommandMessage(11, 2, BrowserTabAction.New, 0),
            TestContext.Current.CancellationToken);
        await session.SendViewCommandAsync(
            new BrowserViewCommandMessage(11, 3, BrowserViewAction.SetZoom, 125),
            TestContext.Current.CancellationToken);
        await session.SendLibraryCommandAsync(
            new BrowserLibraryCommandMessage(11, 4, BrowserLibraryAction.ClearHistory),
            TestContext.Current.CancellationToken);

        var commands = await received.Task.WaitAsync(TestContext.Current.CancellationToken);
        commands[0].ShouldBeOfType<BrowserTabCommandMessage>().CommandId.ShouldBe(2);
        commands[1].ShouldBeOfType<BrowserViewCommandMessage>().CommandId.ShouldBe(3);
        commands[2].ShouldBeOfType<BrowserLibraryCommandMessage>().CommandId.ShouldBe(4);
        await server;
    }

    [Fact]
    public async Task SecureSession_ReceivesProfileStateAndSendsProfileCommands()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new BrowserEndpoint(
            IPAddress.Loopback,
            ((IPEndPoint)listener.LocalEndpoint).Port,
            "receiver-profile-bidirectional");
        var releaseState = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedCommand = new TaskCompletionSource<BrowserProfileCommandMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var profileState = new BrowserProfileStateMessage(
            12,
            1,
            BrowserProfileSource.Tv,
            "family",
            "Windows PC",
            ValueList<BrowserProfileEntry>.From([new BrowserProfileEntry("family", "Family")]));
        var server = ServeAsync(listener, certificate, async stream =>
        {
            var negotiated = await CompleteReceiverHelloAsync(stream);
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, negotiated, AvailableCapability(endpoint.Port));
            await releaseState.Task.WaitAsync(TestContext.Current.CancellationToken);
            await WriteFrameAsync(stream, negotiated, profileState);
            receivedCommand.TrySetResult(
                (await ReadFrameAsync(stream)).Message.ShouldBeOfType<BrowserProfileCommandMessage>());
            await WriteFrameAsync(stream, negotiated, new ByeMessage(ByeReason.Normal, "done"));
        });

        await using var session = await BrowserSession.ConnectAsync(
            endpoint,
            "123456",
            new InMemoryBrowserTrustStore(),
            new RecordingTrustPrompter(accepted: true),
            cancellationToken: TestContext.Current.CancellationToken);
        var receivedState = new TaskCompletionSource<BrowserProfileStateMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.ProfileReceived += message => receivedState.TrySetResult(message);
        releaseState.TrySetResult();
        (await receivedState.Task.WaitAsync(TestContext.Current.CancellationToken)).ShouldBe(profileState);

        var command = new BrowserProfileCommandMessage(
            12,
            2,
            BrowserProfileAction.SelectTvProfile,
            "family");
        await session.SendProfileCommandAsync(command, TestContext.Current.CancellationToken);

        (await receivedCommand.Task.WaitAsync(TestContext.Current.CancellationToken)).ShouldBe(command);
        await server;
    }

    [Fact]
    public async Task ConnectAsync_RejectedFirstUseNeverWritesHelloOrAuth()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new BrowserEndpoint(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, "receiver-test-a");
        var receivedBrowserFrame = false;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            await using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                await stream.AuthenticateAsServerAsync(ServerOptions(certificate), TestContext.Current.CancellationToken);
                receivedBrowserFrame = await TryReadAnyFrameAsync(stream, TestContext.Current.CancellationToken);
            }
            catch (AuthenticationException)
            {
                // Expected: the client rejected this previously untrusted certificate during TLS.
            }
            catch (IOException)
            {
                // Schannel may report a rejected TLS certificate as a stream decryption failure.
            }
        }, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<BrowserTrustException>(() => BrowserSession.ConnectAsync(
            endpoint,
            "123456",
            new InMemoryBrowserTrustStore(),
            new RecordingTrustPrompter(accepted: false),
            cancellationToken: TestContext.Current.CancellationToken));

        receivedBrowserFrame.ShouldBeFalse();
        await server;
    }

    [Fact]
    public async Task ConnectAsync_MismatchedPersistedPinHasNoBypassOrPrompt()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new BrowserEndpoint(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, "receiver-test-a");
        var store = new InMemoryBrowserTrustStore();
        await store.SaveAsync(
            new BrowserTrustedReceiver(
                endpoint.ReceiverIdentity,
                BrowserFingerprint.FromSubjectPublicKeyInfo([9, 9, 9]),
                DateTimeOffset.UtcNow),
            TestContext.Current.CancellationToken);
        var prompt = new RecordingTrustPrompter(accepted: true);
        var server = ServeAuthenticationFailureAsync(listener, certificate);

        await Should.ThrowAsync<BrowserTrustException>(() => BrowserSession.ConnectAsync(
            endpoint,
            "123456",
            store,
            prompt,
            cancellationToken: TestContext.Current.CancellationToken));

        prompt.CallCount.ShouldBe(0);
        await server;
    }

    [Fact]
    public async Task SecureReceiveLoop_ClosesWhenAPlaintextCastMessageAppearsInsideTls()
    {
        using var certificate = CreateCertificate();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = new BrowserEndpoint(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, "receiver-test-a");
        var server = ServeAsync(listener, certificate, async stream =>
        {
            var negotiated = await CompleteReceiverHelloAsync(stream);
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, negotiated, AvailableCapability(endpoint.Port));
            await WriteFrameAsync(stream, negotiated, new PlaybackStateMessage(PlaybackState.Playing));
        });

        await using var session = await BrowserSession.ConnectAsync(
            endpoint,
            "123456",
            new InMemoryBrowserTrustStore(),
            new RecordingTrustPrompter(accepted: true),
            cancellationToken: TestContext.Current.CancellationToken);

        await Should.ThrowAsync<BrowserProtocolException>(() => session.Completion.WaitAsync(TestContext.Current.CancellationToken));
        await server;
    }

    private static async Task ServeAsync(
        TcpListener listener,
        X509Certificate2 certificate,
        Func<SslStream, Task> handle)
    {
        using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        await stream.AuthenticateAsServerAsync(ServerOptions(certificate), TestContext.Current.CancellationToken);
        await handle(stream);
    }

    private static async Task ServeAuthenticationFailureAsync(TcpListener listener, X509Certificate2 certificate)
    {
        using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
        await using var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        try
        {
            await stream.AuthenticateAsServerAsync(ServerOptions(certificate), TestContext.Current.CancellationToken);
        }
        catch (AuthenticationException)
        {
            // A pin mismatch must fail during TLS, before a framed browser payload is readable.
        }
    }

    private static SslServerAuthenticationOptions ServerOptions(X509Certificate2 certificate) => new()
    {
        ServerCertificate = certificate,
        EnabledSslProtocols = SslProtocols.Tls12,
        ClientCertificateRequired = false,
    };

    private static HelloMessage ReceiverHello() => new(
        2,
        ProtocolVersion.Current,
        "Test Fire TV",
        ValueList<CodecId>.From([CodecId.H264]),
        1920,
        1080,
        320);

    private static int NegotiateBrowserProtocol(HelloMessage hostHello) =>
        ProtocolVersion.Negotiate(2, ProtocolVersion.Current, hostHello.MinimumVersion, hostHello.MaximumVersion)
            .ShouldNotBeNull();

    private static async Task<int> CompleteReceiverHelloAsync(Stream stream)
    {
        var hostFrame = await ReadFrameAsync(stream);
        var hostHello = hostFrame.Message.ShouldBeOfType<HelloMessage>();
        var negotiated = NegotiateBrowserProtocol(hostHello);
        await WriteFrameAsync(stream, negotiated, ReceiverHello());
        return negotiated;
    }

    private static BrowserCapabilityMessage AvailableCapability(int port) => new(
        BrowserCapabilityStatus.Available,
        port,
        28,
        "test-webview",
        PreviewSupported: false,
        PreviewMaxWidth: 0,
        PreviewMaxHeight: 0,
        InteractivePreviewFramesPerSecond: 0,
        IdlePreviewFramesPerSecond: 0,
        PreviewMaxBytes: 0);

    private static TestCertificate CreateCertificate()
    {
        return TestCertificate.Create();
    }

    private static async Task<WireFrame> ReadFrameAsync(Stream stream)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, TestContext.Current.CancellationToken);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(prefix);
        var encoded = new byte[4 + length];
        prefix.CopyTo(encoded, 0);
        await stream.ReadExactlyAsync(encoded.AsMemory(4), TestContext.Current.CancellationToken);
        return WireCodec.Decode(encoded);
    }

    private static Task WriteFrameAsync(Stream stream, WireMessage message) =>
        WriteFrameAsync(stream, 2, message);

    private static Task WriteFrameAsync(Stream stream, int protocolVersion, WireMessage message) =>
        stream.WriteAsync(WireCodec.Encode(new WireFrame(protocolVersion, message)), TestContext.Current.CancellationToken).AsTask();

    private static async Task<bool> TryReadAnyFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var first = new byte[1];
        return await stream.ReadAsync(first, cancellationToken) > 0;
    }

    private sealed class RecordingTrustPrompter(bool accepted) : IBrowserTrustPrompter
    {
        public int CallCount { get; private set; }
        public string? Seen { get; private set; }

        public ValueTask<bool> ConfirmFirstUseAsync(
            BrowserPeerIdentity identity,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Seen = identity.Fingerprint.DisplayCode;
            return ValueTask.FromResult(accepted);
        }
    }

    private sealed class TestCertificate : IDisposable
    {
        private readonly string keyName;

        private TestCertificate(X509Certificate2 certificate, string keyName)
        {
            Certificate = certificate;
            this.keyName = keyName;
        }

        internal X509Certificate2 Certificate { get; }

        internal static TestCertificate Create()
        {
            var keyName = "FlintBrowserTlsTest-" + Guid.NewGuid().ToString("N");
            CngKey? key = null;
            try
            {
                key = CngKey.Create(
                    CngAlgorithm.Rsa,
                    keyName,
                    new CngKeyCreationParameters
                    {
                        Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                        KeyUsage = CngKeyUsages.Signing,
                        ExportPolicy = CngExportPolicies.AllowExport,
                    });
                using var rsa = new RSACng(key);
                var request = new CertificateRequest(
                    "CN=Flint Browser Test",
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                return new TestCertificate(
                    request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1)),
                    keyName);
            }
            catch (CryptographicException exception)
            {
                key?.Delete();
                // The normal suite must not mutate a restricted CI/sandbox key store merely to
                // manufacture a Schannel server certificate. The pure trust tests still run; the
                // loopback TLS integration test is explicitly skipped with the platform reason.
                throw SkipException.ForSkip($"A temporary Schannel test key is unavailable: {exception.Message}");
            }
            catch
            {
                key?.Delete();
                throw;
            }
            finally
            {
                key?.Dispose();
            }
        }

        public void Dispose()
        {
            Certificate.Dispose();
            try
            {
                using var key = CngKey.Open(keyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
                key.Delete();
            }
            catch (CryptographicException)
            {
                // It is already gone if certificate construction failed part-way through.
            }
        }

        public static implicit operator X509Certificate2(TestCertificate value) => value.Certificate;
    }
}
