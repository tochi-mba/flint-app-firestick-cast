using System.Net;
using System.Net.Sockets;
using Flint.Core;
using Flint.Protocol;

namespace Flint.Session;

/// <summary>Authenticated TCP session between the Windows host and a Flint receiver.</summary>
public sealed partial class CastSession : IMirrorTransport, IMirrorFeedbackTransport, IAsyncDisposable
{
    public const int DefaultPort = 47_855;

    /// <summary>How long closing waits for a frame already being written to finish.</summary>
    private static readonly TimeSpan FrameFinishWait = TimeSpan.FromSeconds(2);

    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly CancellationTokenSource receiveCancellationTokenSource = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly Task receiveLoop;
    private readonly TaskCompletionSource<Exception?> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<CastSessionClosed> ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool isConnected = true;
    private volatile bool closingHere;
    private int disposeStarted;
    private long controlSequence;

    private CastSession(
        TcpClient client,
        NetworkStream stream,
        WireFrame peerHello,
        CodecId videoCodec,
        int negotiatedProtocolVersion,
        int? browserSecureEndpointPort)
    {
        this.client = client;
        this.stream = stream;
        PeerHello = peerHello;
        VideoCodec = videoCodec;
        NegotiatedProtocolVersion = negotiatedProtocolVersion;
        BrowserSecureEndpointPort = browserSecureEndpointPort;
        receiveLoop = ReceiveAsync(receiveCancellationTokenSource.Token);
    }

    public WireFrame PeerHello { get; }
    public CodecId VideoCodec { get; }

    /// <summary>The exact non-browser wire version selected with this legacy receiver.</summary>
    public int NegotiatedProtocolVersion { get; }

    /// <summary>
    /// Dedicated TLS browser listener port advertised on the AUTH-ack, when the receiver included
    /// one. Used to autofill Web without multicast.
    /// </summary>
    public int? BrowserSecureEndpointPort { get; }

    public bool IsConnected => isConnected && client.Connected;
    public IPAddress LocalAddress => ((IPEndPoint)client.Client.LocalEndPoint!).Address;
    public IPAddress RemoteAddress => ((IPEndPoint)client.Client.RemoteEndPoint!).Address;

    /// <summary>Raised whenever the receiver reports a media-player state.</summary>
    public event Action<PlaybackStateMessage>? PlaybackStateReceived;

    /// <summary>Raised whenever the receiver reports mirror decoder counters.</summary>
    public event Action<StatsMessage>? StatsReceived;

    /// <summary>Raised when the person stops a share with the TV's own remote.</summary>
    /// <remarks>
    /// The TV has already gone back to its own screen, so a share still running would capture and
    /// send a picture nobody can see. Raised on the receive loop's thread.
    /// </remarks>
    public event Action? ShareStoppedOnTv;

    /// <summary>How the session ended, once it has.</summary>
    /// <remarks>
    /// A task rather than only an event, so a listener that attaches after the TV has already gone
    /// still learns that it went, and why.
    /// </remarks>
    public Task<CastSessionClosed> WhenClosed => ended.Task;

    /// <summary>Raised once, on the receive loop's thread, when the session ends.</summary>
    /// <remarks>A handler that throws does not stop the session's own cleanup or the next handler.</remarks>
    public event Action<CastSessionClosed>? Closed;

    /// <summary>Connects and completes the receiver handshake with a pairing code.</summary>
    public static Task<CastSession> ConnectAsync(
        IPAddress address,
        int port,
        string pairingCode,
        string deviceName = "Flint Windows Host",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        return ConnectCoreAsync(address, port, AuthMethod.PairingCode, pairingCode, expectedTvName: null, deviceName, cancellationToken);
    }

    /// <summary>
    /// Connects with a token the TV granted at an earlier pairing, which it accepts in place of a
    /// code until its app restarts or someone asks it for a new code.
    /// </summary>
    /// <param name="address">Where the TV was last seen.</param>
    /// <param name="port">The receiver's port.</param>
    /// <param name="token">The token from <see cref="GrantedToken"/>.</param>
    /// <param name="expectedTvName">
    /// The name the TV gave when the token was granted. The token is sent only to a TV that gives
    /// the same name in its greeting, which it does before anything is sent, so a different device
    /// that now answers at the address never sees it.
    /// </param>
    /// <param name="deviceName">This PC's name, as the TV shows it.</param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    /// <exception cref="CastTvMismatchException">A different TV answered.</exception>
    /// <exception cref="CastAuthenticationRejectedException">The TV no longer accepts the token.</exception>
    public static Task<CastSession> ConnectWithTokenAsync(
        IPAddress address,
        int port,
        string token,
        string expectedTvName,
        string deviceName = "Flint Windows Host",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTvName);
        return ConnectCoreAsync(address, port, AuthMethod.SessionToken, token, expectedTvName, deviceName, cancellationToken);
    }

    /// <summary>Whether <paramref name="token"/> has the shape the TV's tokens have: 43 URL-safe base64 characters.</summary>
    public static bool IsWellFormedToken(string? token) =>
        token is { Length: 43 } && token.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    /// <summary>How long the greeting and login may take once the TV has taken the connection.</summary>
    internal static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    private static async Task<CastSession> ConnectCoreAsync(
        IPAddress address,
        int port,
        AuthMethod method,
        string credential,
        string? expectedTvName,
        string deviceName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);

        // The handshake has its own limit: a device that takes the connection and never answers
        // would otherwise leave "Pairing with the receiver" on screen for as long as Flint ran.
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshake.CancelAfter(HandshakeTimeout);
        var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        try
        {
            client.Client.Bind(new IPEndPoint(ResolveLocalAddress(address, port), 0));
            await client.ConnectAsync(address, port, handshake.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            // HELLO's payload advertises the range this host understands, but its envelope stays
            // v1. An old receiver must be able to parse this first exchange in order to negotiate
            // v1; sending a v2 envelope optimistically would make the compatibility range moot.
            await WriteFrameAsync(stream, new WireFrame(ProtocolVersion.MinSupported, CreateHello(deviceName)), handshake.Token)
                .ConfigureAwait(false);
            var peerHello = await ReadFrameAsync(stream, handshake.Token).ConfigureAwait(false);
            if (peerHello.Message is not HelloMessage hello)
            {
                throw new WireFormatException("Receiver did not answer with HELLO.");
            }

            var negotiatedVersion = ProtocolVersion.Negotiate(
                ProtocolVersion.MinSupported,
                ProtocolVersion.Current,
                hello.MinimumVersion,
                hello.MaximumVersion)
                ?? throw new WireFormatException("The receiver has no compatible protocol version.");

            if (expectedTvName is not null && !string.Equals(hello.DeviceName, expectedTvName, StringComparison.Ordinal))
            {
                throw new CastTvMismatchException(expectedTvName, hello.DeviceName);
            }

            var codec = SelectCodec(hello);
            await WriteFrameAsync(stream, new WireFrame(negotiatedVersion, new AuthMessage(
                method,
                BinaryData.From(System.Text.Encoding.ASCII.GetBytes(credential)))), handshake.Token)
                .ConfigureAwait(false);
            var authReply = await ReadFrameAsync(stream, handshake.Token).ConfigureAwait(false);
            if (authReply.Message is ByeMessage bye)
            {
                throw bye.Reason is ByeReason.AuthenticationFailed
                    ? new CastAuthenticationRejectedException(bye.Detail)
                    : new WireFormatException($"Receiver rejected the session: {bye.Detail}");
            }

            var browserPort = TryReadBrowserPortHint(authReply.Message);
            return new CastSession(client, stream, peerHello, codec, negotiatedVersion, browserPort)
            {
                GrantedToken = TryReadGrantedToken(authReply.Message),
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException(
                $"The TV did not finish connecting within {HandshakeTimeout.TotalSeconds:0} seconds. Check that Flint is open on it.");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The token the TV granted when this session was established, or null when its reply carried
    /// none. Kept so a later connection can log in without a code: see <see cref="ConnectWithTokenAsync"/>.
    /// </summary>
    /// <remarks>Authorisation, not encryption, and never logged.</remarks>
    public string? GrantedToken { get; private init; }

    /// <summary>The token in an AUTH reply, when it carries one of the right shape.</summary>
    internal static string? TryReadGrantedToken(WireMessage message)
    {
        if (message is not AuthMessage { Method: AuthMethod.SessionToken, Credential: var credential })
        {
            return null;
        }

        var text = System.Text.Encoding.ASCII.GetString(credential.Span);
        return IsWellFormedToken(text) ? text : null;
    }

    /// <summary>
    /// SESSION_TOKEN AUTH-acks may carry the browser TLS port in the optional fingerprint field.
    /// </summary>
    public static int? TryReadBrowserPortHint(WireMessage message)
    {
        if (message is not AuthMessage { Method: AuthMethod.SessionToken, PublicKeyFingerprint: { } hint })
        {
            return null;
        }

        if (!int.TryParse(hint, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65_535)
        {
            return null;
        }

        return port;
    }

    /// <summary>Sends the decoder configuration before the first video packet.</summary>
    /// <remarks>
    /// The codec is the caller's to state, because only the caller knows what its encoder actually
    /// produced. Defaulting to <see cref="VideoCodec"/> - what the handshake negotiated - is what
    /// let an H.264 mirror be announced as HEVC, which the receiver answered by building an HEVC
    /// decoder and failing on the first access unit.
    /// </remarks>
    public Task SendVideoConfigAsync(
        CodecId codec,
        int width,
        int height,
        IEnumerable<BinaryData> codecSpecificData,
        CancellationToken cancellationToken = default) =>
        SendAsync(new VideoConfigMessage(
            codec,
            width,
            height,
            ValueList<BinaryData>.From(codecSpecificData)), cancellationToken);

    /// <summary>Sends one encoded access unit without waiting for a later frame.</summary>
    public Task SendVideoAsync(
        long presentationTimeUs,
        bool keyFrame,
        BinaryData data,
        CancellationToken cancellationToken = default) =>
        SendAsync(new VideoPacket(presentationTimeUs, keyFrame, data), cancellationToken);

    /// <summary>Selects the receiver render surface.</summary>
    public Task SendSurfaceAsync(SurfaceMessage surface, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return SendAsync(surface, cancellationToken);
    }

    /// <summary>Sends a legacy receiver control event on the negotiated ordinary channel.</summary>
    public Task SendControlAsync(ControlMessage control, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(control);
        return SendAsync(control, cancellationToken);
    }

    /// <summary>Says goodbye to the TV, then closes the session.</summary>
    /// <remarks>
    /// The goodbye lets the TV go back to waiting at once instead of noticing a dead socket later.
    /// Safe to call twice, and on a session the TV has already closed: there is then nobody to tell.
    /// </remarks>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        // Marked first: the TV may close its end as soon as it reads the goodbye, and that end must
        // still read as this PC's doing rather than a lost connection.
        closingHere = true;
        if (IsConnected && Volatile.Read(ref disposeStarted) == 0)
        {
            try
            {
                await SendAsync(new ByeMessage(ByeReason.Normal, "Disconnected on Windows."), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The TV went first, or the goodbye was abandoned. Closing is all that is left.
            }
        }

        await DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        closingHere = true;
        isConnected = false;
        receiveCancellationTokenSource.Cancel();

        // Let an already-started frame finish before closing the stream. New senders observe
        // disposeStarted either before or after entering the gate and fail without writing. A TV
        // that has stopped reading can hold a write open indefinitely, so the wait is bounded:
        // past it, closing the stream is what ends that write.
        var entered = await sendGate.WaitAsync(FrameFinishWait).ConfigureAwait(false);
        try
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            client.Dispose();
        }
        finally
        {
            if (entered)
            {
                sendGate.Release();
            }
        }
        try
        {
            await receiveLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            receiveCancellationTokenSource.Dispose();

            // A write that outlasted the wait still holds the gate and releases it when its stream
            // fails; disposing the gate under it would turn that failure into a different one.
            if (entered)
            {
                sendGate.Dispose();
            }
        }
    }

    private async Task SendAsync(WireMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (BrowserWireRules.IsForbiddenOnOrdinaryChannel(message))
        {
            throw new InvalidOperationException(
                "Browser messages and the browser surface require the separate TLS BrowserSession.");
        }

        if (Volatile.Read(ref disposeStarted) != 0 || !IsConnected)
        {
            throw new IOException("The receiver session is no longer connected.");
        }

        await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref disposeStarted) != 0 || !IsConnected)
            {
                throw new IOException("The receiver session is no longer connected.");
            }

            // One logical frame must be one uninterrupted stream write. Mirror, media, and UI
            // controls can all send concurrently, and interleaving their length-prefixed frames
            // makes the receiver reject an otherwise healthy session.
            await WriteFrameAsync(stream, new WireFrame(NegotiatedProtocolVersion, message), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            sendGate.Release();
        }
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        ByeMessage? goodbye = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frame = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                if (BrowserWireRules.IsForbiddenOnOrdinaryChannel(frame.Message))
                {
                    throw new WireFormatException(
                        "Browser traffic was received on the ordinary CastSession channel.");
                }

                if (frame.Message is PlaybackStateMessage playback)
                {
                    PlaybackStateReceived?.Invoke(playback);
                }

                if (frame.Message is StatsMessage stats)
                {
                    StatsReceived?.Invoke(stats);
                }

                if (frame.Message is ControlMessage { Event: TransportControl { Action: TransportAction.Stop } })
                {
                    ShareStoppedOnTv?.Invoke();
                }

                if (frame.Message is ByeMessage bye)
                {
                    goodbye = bye;
                    break;
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
            isConnected = false;
            closed.TrySetResult(failure);
            var outcome = Classify(goodbye, failure);
            if (ended.TrySetResult(outcome))
            {
                FlintDiag.Info("FlintCast", $"session closed reason={outcome.Reason}");
                RaiseClosed(outcome);
            }
        }
    }

    /// <summary>Why the session ended, from what the receive loop saw last.</summary>
    private CastSessionClosed Classify(ByeMessage? goodbye, Exception? failure) =>
        closingHere || Volatile.Read(ref disposeStarted) != 0 ? new CastSessionClosed(CastSessionEnd.ClosedByThisPc)
        : goodbye is not null ? new CastSessionClosed(CastSessionEnd.EndedByTv, string.IsNullOrWhiteSpace(goodbye.Detail) ? null : goodbye.Detail)
        : failure is WireFormatException ? new CastSessionClosed(CastSessionEnd.ProtocolError)
        : new CastSessionClosed(CastSessionEnd.ConnectionLost);

    private void RaiseClosed(CastSessionClosed outcome)
    {
        foreach (var handler in Closed?.GetInvocationList().Cast<Action<CastSessionClosed>>() ?? [])
        {
            try
            {
                handler(outcome);
            }
#pragma warning disable CA1031 // A listener's failure is the listener's; the session still has to finish closing.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                FlintDiag.Warn("FlintCast", $"session closed handler failed: {exception.GetType().Name}");
            }
        }
    }

    /// <summary>
    /// Codecs this host can actually encode and send.
    /// </summary>
    /// <remarks>
    /// H.264 alone, because that is all the engine produces: both the hardware and the software
    /// encoder are H.264, and the engine refuses any other codec outright.
    /// <para>
    /// This list used to name H.265 first, and cost an afternoon. The receiver advertises H.265,
    /// so negotiation chose it, the handshake announced H.265, and the engine then sent H.264. Every
    /// counter on the host stayed perfectly healthy - sixty frames, 1.5 MB, zero recoveries - while
    /// the television never left its pairing screen, because a decoder configured for HEVC cannot
    /// do anything with AVC. Nothing failed anywhere near where the mistake was.
    /// </para>
    /// <para>
    /// So this list is a statement about the encoder, not a wish. Adding H.265 here without an
    /// H.265 encoder behind it reintroduces exactly that silence.
    /// </para>
    /// </remarks>
    private static readonly CodecId[] HostVideoCodecs = [CodecId.H264];

    private static HelloMessage CreateHello(string deviceName) => new(
        ProtocolVersion.MinSupported,
        ProtocolVersion.Current,
        deviceName,
        ValueList<CodecId>.From(HostVideoCodecs),
        1920,
        1080,
        96);

    /// <summary>Asks the route table which local interface reaches the receiver.</summary>
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

    /// <summary>
    /// Chooses a codec both ends can handle.
    /// </summary>
    /// <remarks>
    /// The intersection of what the receiver decodes and what this host encodes, in the host's
    /// order of preference - never simply the best thing the receiver claims. A receiver's
    /// capabilities say what it could decode if it were sent one, and treating that as a choice is
    /// how a session ends up announcing a codec nothing is going to send.
    /// </remarks>
    /// <exception cref="WireFormatException">
    /// When there is no overlap, which is a refusal rather than a fallback: sending a stream the
    /// receiver cannot decode produces a blank television and a host that reports success.
    /// </exception>
    internal static CodecId SelectCodec(HelloMessage hello)
    {
        foreach (var codec in HostVideoCodecs)
        {
            if (hello.CodecCapabilities.Any(candidate => candidate == codec))
            {
                return codec;
            }
        }

        throw new WireFormatException(
            "The receiver decodes none of the codecs this host can encode "
            + $"({string.Join(", ", HostVideoCodecs.Select(codec => codec.Value))}).");
    }

    private static async Task WriteFrameAsync(
        NetworkStream stream,
        WireFrame frame,
        CancellationToken cancellationToken)
    {
        var bytes = WireCodec.Encode(frame);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WireFrame> ReadFrameAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await stream.ReadExactlyAsync(lengthBytes, cancellationToken).ConfigureAwait(false);
        var bodyLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lengthBytes);
        if (bodyLength < WireCodec.EnvelopeLength || bodyLength > WireCodec.MaxFrameLength)
        {
            throw new WireFormatException($"Invalid receiver frame length: {bodyLength}.");
        }

        var bytes = new byte[4 + bodyLength];
        lengthBytes.CopyTo(bytes, 0);
        await stream.ReadExactlyAsync(bytes.AsMemory(4), cancellationToken).ConfigureAwait(false);
        return WireCodec.Decode(bytes);
    }
}
