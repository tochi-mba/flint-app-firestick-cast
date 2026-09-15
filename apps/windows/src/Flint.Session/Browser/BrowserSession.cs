using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Flint.Core;
using Flint.Protocol;

namespace Flint.Session.Browser;

/// <summary>
/// The only Windows transport allowed to carry browser protocol v2 messages.
/// </summary>
/// <remarks>
/// It has a dedicated IPv4 TCP listener route, TLS 1.2-or-newer, SPKI pinning, and pairing inside
/// TLS. It deliberately has no plaintext fallback and no cast/media API; <see cref="CastSession"/>
/// remains the legacy ordinary-channel transport.
/// </remarks>
public sealed class BrowserSession : IAsyncDisposable
{
    private const string TlsTargetHost = "flint-browser";
    private const int BrowserProtocolMinimum = 2;
    private readonly TcpClient client;
    private readonly SslStream stream;
    private readonly CancellationTokenSource receiveCancellation = new();
    private readonly BrowserReliableWriter writer;
    private readonly int negotiatedProtocolVersion;
    private readonly Task receiveLoop;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int connected = 1;
    private int disposeStarted;

    private BrowserSession(
        TcpClient client,
        SslStream stream,
        BrowserPeerIdentity peerIdentity,
        BrowserCapabilityMessage capability,
        int negotiatedProtocolVersion)
    {
        this.client = client;
        this.stream = stream;
        PeerIdentity = peerIdentity;
        Capability = capability;
        this.negotiatedProtocolVersion = negotiatedProtocolVersion;
        writer = new BrowserReliableWriter(stream, negotiatedProtocolVersion, OnWriterFault);
        receiveLoop = ReceiveAsync(receiveCancellation.Token);
    }

    /// <summary>The selected route; it is still not the source of receiver identity.</summary>
    public BrowserEndpoint Endpoint => PeerIdentity.Endpoint;

    /// <summary>The full pin identity accepted during TLS and its safe short comparison code.</summary>
    public BrowserPeerIdentity PeerIdentity { get; }

    /// <summary>The first authenticated secure capability reply.</summary>
    public BrowserCapabilityMessage Capability { get; }

    /// <summary>The browser wire version negotiated during the TLS hello exchange.</summary>
    public int NegotiatedProtocolVersion => negotiatedProtocolVersion;

    /// <summary>Completes cleanly for a received secure BYE and faults for transport/protocol failure.</summary>
    public Task Completion => completion.Task;

    /// <summary>Whether the TLS session is still usable for a browser command or input event.</summary>
    public bool IsConnected => Volatile.Read(ref connected) != 0 && client.Connected;

    /// <summary>Raised for a bounded, receiver-safe browser state update.</summary>
    public event Action<BrowserStateMessage>? StateReceived;

    /// <summary>Raised for an opt-in bounded JPEG preview after later receiver slices enable it.</summary>
    public event Action<BrowserPreviewMessage>? PreviewReceived;

    /// <summary>Raised when the receiver needs an explicit reply to its current native dialog.</summary>
    public event Action<BrowserDialogMessage>? DialogReceived;

    /// <summary>Raised with the receiver's full replacement snapshot of its open tabs.</summary>
    public event Action<BrowserTabStateMessage>? TabsReceived;

    /// <summary>Raised with the receiver's zoom, user-agent, display and find state.</summary>
    public event Action<BrowserViewStateMessage>? ViewReceived;

    /// <summary>Raised with a bounded PNG favicon for a tab or library entry.</summary>
    public event Action<BrowserFaviconMessage>? FaviconReceived;

    /// <summary>Raised when the receiver asks the Windows profile library to mutate or resync.</summary>
    public event Action<BrowserLibraryCommandMessage>? LibraryRequestReceived;

    /// <summary>Raised with the active TV-owned profile's bounded library projection.</summary>
    public event Action<BrowserLibraryStateMessage>? LibraryReceived;

    /// <summary>Raised with the receiver's named TV profiles and active storage owner.</summary>
    public event Action<BrowserProfileStateMessage>? ProfileReceived;

    /// <summary>Raised when the receiver publishes a network/VPN snapshot (no config secrets).</summary>
    public event Action<BrowserNetworkStateMessage>? NetworkReceived;

    /// <summary>Raised when the receiver publishes a browser workspace snapshot.</summary>
    public event Action<BrowserWorkspaceStateMessage>? WorkspaceReceived;

    /// <summary>Authoritative workspace dividers from a protocol-four receiver.</summary>
    public event Action<BrowserWorkspaceGeometryMessage>? GeometryReceived;

    /// <summary>
    /// Connects without any ordinary-cast retry, verifies the receiver identity, and gets capability.
    /// </summary>
    public static async Task<BrowserSession> ConnectAsync(
        BrowserEndpoint endpoint,
        string pairingCode,
        IBrowserTrustStore trustStore,
        IBrowserTrustPrompter trustPrompter,
        string deviceName = "Flint Windows Browser Remote",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        ArgumentNullException.ThrowIfNull(trustStore);
        ArgumentNullException.ThrowIfNull(trustPrompter);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceName);
        ValidatePairingCode(pairingCode);

        var persistedTrust = await trustStore.FindAsync(endpoint.ReceiverIdentity, cancellationToken)
            .ConfigureAwait(false);
        FlintDiag.Info(
            "FlintTrust",
            $"connect begin identity={endpoint.ReceiverIdentity} endpoint={endpoint.Address}:{endpoint.Port} rememberedPin={(persistedTrust is not null ? "yes" : "no")}");
        var evaluation = new TrustEvaluation(endpoint, persistedTrust, trustPrompter, cancellationToken);
        var client = new TcpClient(endpoint.Address.AddressFamily) { NoDelay = true };
        SslStream? tls = null;
        try
        {
            client.Client.Bind(new IPEndPoint(ResolveLocalAddress(endpoint.Address, endpoint.Port), 0));
            await client.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken).ConfigureAwait(false);
            FlintDiag.Info("FlintTrust", "tcp connected; starting tls");
            tls = new SslStream(client.GetStream(), leaveInnerStreamOpen: false, evaluation.ValidateCertificate);
            try
            {
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = TlsTargetHost,
                    // Fire OS API 25 is the compatibility floor. TLS 1.2 is the common secure
                    // baseline there; the OS may still negotiate a newer default only when this
                    // explicit policy is broadened after target evidence proves it.
                    EnabledSslProtocols = SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    // This browser route never uses mutual TLS. Supplying an explicit empty
                    // collection prevents Schannel from probing ambient user certificates.
                    ClientCertificates = [],
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException) when (evaluation.Failure is not null)
            {
                FlintDiag.Warn("FlintTrust", $"tls rejected: {evaluation.Failure.Message}");
                throw evaluation.Failure;
            }

            var peerIdentity = evaluation.AcceptedIdentity
                ?? throw new BrowserTrustException("The TLS connection completed without an accepted receiver identity.");
            FlintDiag.Info(
                "FlintTrust",
                $"tls accepted displayCode={peerIdentity.Fingerprint.DisplayCode} firstUse={evaluation.FirstUseAccepted}");
            await BrowserFrameStream.WriteAsync(
                    tls,
                    BrowserProtocolMinimum,
                    CreateHello(deviceName),
                    cancellationToken)
                .ConfigureAwait(false);
            var peerHelloFrame = await BrowserFrameStream.ReadAsync(tls, cancellationToken).ConfigureAwait(false);
            if (peerHelloFrame.Message is ByeMessage bye)
            {
                throw new BrowserProtocolException($"The secure receiver rejected HELLO: {SafeDetail(bye.Detail)}");
            }
            if (peerHelloFrame.Message is not HelloMessage hello)
            {
                throw new BrowserProtocolException("The TLS receiver did not answer with HELLO.");
            }

            var negotiatedVersion = ProtocolVersion.Negotiate(
                BrowserProtocolMinimum,
                ProtocolVersion.Current,
                hello.MinimumVersion,
                hello.MaximumVersion)
                ?? throw new BrowserProtocolException(
                    "The TLS receiver did not negotiate a compatible browser protocol version.");
            if (peerHelloFrame.ProtocolVersion != negotiatedVersion)
            {
                throw new BrowserProtocolException(
                    $"The TLS receiver answered HELLO with protocol v{peerHelloFrame.ProtocolVersion}; v{negotiatedVersion} was expected.");
            }

            FlintDiag.Info("FlintTrust", $"hello ok protocol=v{negotiatedVersion}; sending auth");
            await BrowserFrameStream.WriteAsync(
                    tls,
                    negotiatedVersion,
                    new AuthMessage(
                        AuthMethod.PairingCode,
                        BinaryData.From(Encoding.ASCII.GetBytes(pairingCode))),
                    cancellationToken)
                .ConfigureAwait(false);
            var capabilityFrame = await BrowserFrameStream.ReadAsync(tls, cancellationToken).ConfigureAwait(false);
            RequireNegotiatedProtocolFrame(capabilityFrame, negotiatedVersion, "browser capability");
            if (capabilityFrame.Message is ByeMessage rejected)
            {
                FlintDiag.Warn("FlintTrust", $"auth rejected: {SafeDetail(rejected.Detail)}");
                throw new BrowserCapabilityException($"The receiver rejected browser authentication: {SafeDetail(rejected.Detail)}");
            }
            if (capabilityFrame.Message is not BrowserCapabilityMessage capability)
            {
                throw new BrowserProtocolException("The secure receiver did not send a browser capability response.");
            }
            if (capability.Status != BrowserCapabilityStatus.Available)
            {
                throw new BrowserCapabilityException($"Browser capability is unavailable: {SafeDetail(capability.Detail)}");
            }
            if (capability.SecureEndpointPort != endpoint.Port)
            {
                throw new BrowserCapabilityException("The authenticated receiver advertised a changed browser endpoint. Refresh discovery and verify again.");
            }

            if (persistedTrust is null && evaluation.FirstUseAccepted)
            {
                await trustStore.SaveAsync(
                    new BrowserTrustedReceiver(endpoint.ReceiverIdentity, peerIdentity.Fingerprint, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                FlintDiag.Info("FlintTrust", "pin saved for returning reconnect");
            }

            FlintDiag.Info(
                "FlintTrust",
                $"session ready capabilityPort={capability.SecureEndpointPort} protocol=v{negotiatedVersion}");
            var session = new BrowserSession(client, tls, peerIdentity, capability, negotiatedVersion);
            tls = null;
            return session;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            FlintDiag.Error("FlintTrust", $"connect failed: {exception.GetType().Name}: {SafeDetail(exception.Message)}");
            if (tls is not null)
            {
                await tls.DisposeAsync().ConfigureAwait(false);
            }
            client.Dispose();
            throw;
        }
        catch
        {
            if (tls is not null)
            {
                await tls.DisposeAsync().ConfigureAwait(false);
            }
            client.Dispose();
            throw;
        }
    }

    /// <summary>Sends an ordered host-to-receiver browser command over the dedicated TLS writer.</summary>
    public Task SendCommandAsync(BrowserCommandMessage command, CancellationToken cancellationToken = default) =>
        SendAsync(command, cancellationToken);

    /// <summary>Sends an ordered, portable browser input event over the dedicated TLS writer.</summary>
    public Task SendInputAsync(BrowserInputMessage input, CancellationToken cancellationToken = default) =>
        SendAsync(input, cancellationToken);

    /// <summary>Sends a reply for the one active native receiver dialog over the TLS writer.</summary>
    public Task SendDialogReplyAsync(BrowserDialogReplyMessage reply, CancellationToken cancellationToken = default) =>
        SendAsync(reply, cancellationToken);

    /// <summary>Sends a tab operation over the dedicated TLS writer.</summary>
    public Task SendTabCommandAsync(BrowserTabCommandMessage command, CancellationToken cancellationToken = default) =>
        SendAsync(command, cancellationToken);

    /// <summary>Sends a zoom, user-agent, display or find operation over the dedicated TLS writer.</summary>
    public Task SendViewCommandAsync(BrowserViewCommandMessage command, CancellationToken cancellationToken = default) =>
        SendAsync(command, cancellationToken);

    /// <summary>Sends the Windows-current-user library projection to the active receiver.</summary>
    public Task SendLibraryStateAsync(
        BrowserLibraryStateMessage state,
        CancellationToken cancellationToken = default) =>
        SendAsync(state, cancellationToken);

    /// <summary>Sends a mutation or snapshot request to the active TV-owned profile.</summary>
    public Task SendLibraryCommandAsync(
        BrowserLibraryCommandMessage command,
        CancellationToken cancellationToken = default) =>
        SendAsync(command, cancellationToken);

    /// <summary>Sends a named-profile or storage-owner operation to the receiver.</summary>
    public Task SendProfileCommandAsync(
        BrowserProfileCommandMessage command,
        CancellationToken cancellationToken = default) =>
        SendAsync(command, cancellationToken);

    /// <summary>Sends a network/VPN settings operation to the receiver.</summary>
    public Task SendNetworkCommandAsync(
        BrowserNetworkCommandMessage command,
        CancellationToken cancellationToken = default) =>
        SendAsync(command, cancellationToken);

    /// <summary>Sends a negotiated workspace resize.</summary>
    public Task SendWorkspaceResizeAsync(BrowserWorkspaceResizeMessage command, CancellationToken cancellationToken = default)
        => SendAsync(command, cancellationToken);

    /// <summary>Sends a browser workspace command to the receiver.</summary>
    public Task SendWorkspaceCommandAsync(
        BrowserWorkspaceCommandMessage command,
        CancellationToken cancellationToken = default) =>
        SendAsync(command, cancellationToken);

    /// <summary>Sends focused-pane text input to the receiver workspace.</summary>
    public Task SendWorkspaceInputAsync(
        BrowserWorkspaceInputMessage input,
        CancellationToken cancellationToken = default) =>
        SendAsync(input, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref connected, 0);
        receiveCancellation.Cancel();
        await stream.DisposeAsync().ConfigureAwait(false);
        client.Dispose();
        await writer.DisposeAsync().ConfigureAwait(false);
        try
        {
            await receiveLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            completion.TrySetResult();
            receiveCancellation.Dispose();
        }
    }

    private async Task SendAsync(WireMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message is not BrowserCommandMessage
            and not BrowserInputMessage
            and not BrowserDialogReplyMessage
            and not BrowserTabCommandMessage
            and not BrowserViewCommandMessage
            and not BrowserLibraryCommandMessage
            and not BrowserLibraryStateMessage
            and not BrowserProfileCommandMessage
            and not BrowserNetworkCommandMessage
            and not BrowserWorkspaceCommandMessage
            and not BrowserWorkspaceResizeMessage
            and not BrowserWorkspaceInputMessage)
        {
            throw new ArgumentException("Only host-to-receiver browser messages can use BrowserSession.", nameof(message));
        }
        if (!IsConnected || Volatile.Read(ref disposeStarted) != 0)
        {
            throw new IOException("The secure browser session is no longer connected.");
        }

        await writer.EnqueueAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await BrowserFrameStream.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
                RequireNegotiatedProtocolFrame(frame, negotiatedProtocolVersion, "browser message");
                switch (frame.Message)
                {
                    case BrowserStateMessage state:
                        StateReceived?.Invoke(state);
                        break;

                    case BrowserPreviewMessage preview:
                        PreviewReceived?.Invoke(preview);
                        break;

                    case BrowserDialogMessage dialog:
                        DialogReceived?.Invoke(dialog);
                        break;

                    case BrowserTabStateMessage tabs:
                        TabsReceived?.Invoke(tabs);
                        break;

                    case BrowserViewStateMessage view:
                        ViewReceived?.Invoke(view);
                        break;

                    case BrowserFaviconMessage favicon:
                        FaviconReceived?.Invoke(favicon);
                        break;

                    case BrowserLibraryCommandMessage request:
                        LibraryRequestReceived?.Invoke(request);
                        break;

                    case BrowserLibraryStateMessage library:
                        LibraryReceived?.Invoke(library);
                        break;

                    case BrowserProfileStateMessage profile:
                        ProfileReceived?.Invoke(profile);
                        break;

                    case BrowserNetworkStateMessage network:
                        NetworkReceived?.Invoke(network);
                        break;

                    case BrowserWorkspaceGeometryMessage geometry:
                        GeometryReceived?.Invoke(geometry);
                        break;
                    case BrowserWorkspaceStateMessage workspace:
                        WorkspaceReceived?.Invoke(workspace);
                        break;

                    case ByeMessage:
                        return;

                    default:
                        throw new BrowserProtocolException(
                            $"Message type {frame.Message.TypeId} is not valid from receiver to host on BrowserSession.");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or WireFormatException)
        {
            failure = exception;
        }
        finally
        {
            Interlocked.Exchange(ref connected, 0);
            if (failure is not null)
            {
                writer.Fail(failure);
                completion.TrySetException(failure);
            }
            else
            {
                completion.TrySetResult();
            }
        }
    }

    private void OnWriterFault(Exception exception)
    {
        if (Interlocked.Exchange(ref connected, 0) != 0)
        {
            receiveCancellation.Cancel();
            completion.TrySetException(exception);
        }
    }

    private static void RequireNegotiatedProtocolFrame(WireFrame frame, int negotiatedProtocolVersion, string purpose)
    {
        if (negotiatedProtocolVersion < BrowserProtocolMinimum
            || frame.ProtocolVersion != negotiatedProtocolVersion)
        {
            throw new BrowserProtocolException(
                $"The secure browser {purpose} used protocol v{frame.ProtocolVersion}; v{negotiatedProtocolVersion} was negotiated.");
        }
    }

    private static HelloMessage CreateHello(string deviceName) => new(
        BrowserProtocolMinimum,
        ProtocolVersion.Current,
        deviceName,
        ValueList<CodecId>.From([CodecId.H264]),
        1920,
        1080,
        96);

    private static void ValidatePairingCode(string pairingCode)
    {
        if (pairingCode.Length != 6 || pairingCode.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException("Browser pairing code must be exactly six decimal digits.", nameof(pairingCode));
        }
    }

    private static string SafeDetail(string detail) => string.IsNullOrWhiteSpace(detail)
        ? "no additional detail"
        : detail.Length <= BrowserWireLimits.MaxDetailBytes
            ? detail
            : "receiver reported an invalid detail";

    private static IPAddress ResolveLocalAddress(IPAddress remoteAddress, int remotePort)
    {
        using var routeProbe = new Socket(remoteAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        routeProbe.Connect(new IPEndPoint(remoteAddress, remotePort));
        if (routeProbe.LocalEndPoint is not IPEndPoint local
            || local.Address.Equals(IPAddress.Any)
            || local.Address.Equals(IPAddress.IPv6Any))
        {
            throw new IOException($"Windows could not select a local interface for {remoteAddress}.");
        }

        return local.Address;
    }

    private sealed class TrustEvaluation(
        BrowserEndpoint endpoint,
        BrowserTrustedReceiver? persistedTrust,
        IBrowserTrustPrompter trustPrompter,
        CancellationToken cancellationToken)
    {
        private int evaluated;

        internal BrowserTrustException? Failure { get; private set; }
        internal BrowserPeerIdentity? AcceptedIdentity { get; private set; }
        internal bool FirstUseAccepted { get; private set; }

        internal bool ValidateCertificate(
            object _,
            X509Certificate? certificate,
            X509Chain? __,
            SslPolicyErrors errors)
        {
            if (Interlocked.Exchange(ref evaluated, 1) != 0)
            {
                return AcceptedIdentity is not null;
            }

            try
            {
                if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                {
                    throw new BrowserTrustException("The receiver did not present a browser certificate.");
                }
                var unsupportedErrors = errors & ~(SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch);
                if (unsupportedErrors != SslPolicyErrors.None)
                {
                    throw new BrowserTrustException("The receiver certificate failed TLS validation.");
                }

                using var receiverCertificate = new X509Certificate2(certificate);
                var now = DateTimeOffset.UtcNow;
                if (receiverCertificate.NotBefore.ToUniversalTime() > now.UtcDateTime
                    || receiverCertificate.NotAfter.ToUniversalTime() <= now.UtcDateTime)
                {
                    throw new BrowserTrustException("The receiver browser certificate is not currently valid.");
                }

                var identity = new BrowserPeerIdentity(endpoint, BrowserFingerprint.FromCertificate(receiverCertificate));
                if (persistedTrust is not null)
                {
                    if (!persistedTrust.Fingerprint.Matches(identity.Fingerprint))
                    {
                        FlintDiag.Warn(
                            "FlintTrust",
                            $"pin mismatch remembered={persistedTrust.Fingerprint.DisplayCode} presented={identity.Fingerprint.DisplayCode}");
                        throw new BrowserTrustException(
                            "The receiver browser identity changed. Forget it explicitly before verifying a new identity.");
                    }

                    AcceptedIdentity = identity;
                    return true;
                }

                var accepted = trustPrompter.ConfirmFirstUseAsync(identity, cancellationToken).GetAwaiter().GetResult();
                if (!accepted)
                {
                    throw new BrowserTrustException("Browser identity verification was declined.");
                }

                AcceptedIdentity = identity;
                FirstUseAccepted = true;
                return true;
            }
            catch (BrowserTrustException exception)
            {
                Failure = exception;
                return false;
            }
            catch (Exception exception)
            {
                Failure = new BrowserTrustException("The receiver browser certificate could not be verified.", exception);
                return false;
            }
        }
    }
}
