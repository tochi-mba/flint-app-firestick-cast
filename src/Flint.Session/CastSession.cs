using System.Net;
using System.Net.Sockets;
using Flint.Protocol;

namespace Flint.Session;

/// <summary>Authenticated TCP session between the Windows host and a Flint receiver.</summary>
public sealed class CastSession : IMirrorTransport, IMirrorFeedbackTransport, IAsyncDisposable
{
    public const int DefaultPort = 47_855;

    /// <summary>
    /// Bytes read per <see cref="MediaDataMessage"/> chunk when pushing a file. Comfortably under
    /// <see cref="WireCodec.MaxFrameLength"/> so a chunk is never rejected as oversized, and large
    /// enough that a multi-megabyte file does not need thousands of round trips to send.
    /// </summary>
    private const int PushChunkBytes = 512 * 1024;

    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly CancellationTokenSource receiveCancellationTokenSource = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly Task receiveLoop;
    private readonly TaskCompletionSource<Exception?> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool isConnected = true;
    private int disposeStarted;

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

    /// <summary>Connects and completes the receiver handshake with a pairing code.</summary>
    public static async Task<CastSession> ConnectAsync(
        IPAddress address,
        int port,
        string pairingCode,
        string deviceName = "Flint Windows Host",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65_535);

        var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        try
        {
            client.Client.Bind(new IPEndPoint(ResolveLocalAddress(address, port), 0));
            await client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            // HELLO's payload advertises the range this host understands, but its envelope stays
            // v1. An old receiver must be able to parse this first exchange in order to negotiate
            // v1; sending a v2 envelope optimistically would make the compatibility range moot.
            await WriteFrameAsync(stream, new WireFrame(ProtocolVersion.MinSupported, CreateHello(deviceName)), cancellationToken)
                .ConfigureAwait(false);
            var peerHello = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
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

            var codec = SelectCodec(hello);
            await WriteFrameAsync(stream, new WireFrame(negotiatedVersion, new AuthMessage(
                AuthMethod.PairingCode,
                BinaryData.From(System.Text.Encoding.ASCII.GetBytes(pairingCode)))), cancellationToken)
                .ConfigureAwait(false);
            var authReply = await ReadFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (authReply.Message is ByeMessage bye)
            {
                throw new WireFormatException($"Receiver rejected the session: {bye.Detail}");
            }

            var browserPort = TryReadBrowserPortHint(authReply.Message);
            return new CastSession(client, stream, peerHello, codec, negotiatedVersion, browserPort);
        }
        catch
        {
            client.Dispose();
            throw;
        }
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
    /// produced. Defaulting to <see cref="VideoCodec"/> — what the handshake negotiated — is what
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

    /// <summary>Sends a local-media command for the receiver to fetch and play.</summary>
    public Task SendMediaAsync(MediaCommandMessage command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SendAsync(command, cancellationToken);
    }

    /// <summary>Sends media and waits for the receiver to confirm playback or report an error.</summary>
    public async Task<PlaybackStateMessage> SendMediaAndWaitForPlaybackStartAsync(
        MediaCommandMessage command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var playbackTask = WaitForPlaybackStartAsync(cancellationToken);
        await SendMediaAsync(command, cancellationToken).ConfigureAwait(false);
        return await playbackTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a local file's bytes over this connection in order, then asks the receiver to play it.
    /// </summary>
    /// <remarks>
    /// Some receivers cannot reach the host at all: several Fire OS builds silently drop an outbound
    /// connection the receiver app itself opens to a private LAN address, even though the exact same
    /// address works perfectly for this connection (host to receiver — which is how pairing already
    /// succeeded). Pushing the bytes over the connection that is proven to work sidesteps that
    /// restriction entirely, at the cost of buffering the whole file into memory on the receiver
    /// rather than streaming it — acceptable for the files this path is meant for.
    /// </remarks>
    public async Task<PlaybackStateMessage> PushMediaAndWaitForPlaybackStartAsync(
        string filePath,
        string title,
        string mimeType,
        long durationMs = -1,
        long startPositionMs = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        var playbackTask = WaitForPlaybackStartAsync(cancellationToken);
        await using (var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            PushChunkBytes,
            useAsync: true))
        {
            var totalBytes = file.Length;
            var sentBytes = 0L;
            var buffer = new byte[PushChunkBytes];
            int bytesRead;
            while ((bytesRead = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                sentBytes += bytesRead;
                var isFinal = sentBytes >= totalBytes;
                await SendAsync(
                    new MediaDataMessage(BinaryData.From(buffer.AsSpan(0, bytesRead)), isFinal),
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(totalBytes > 0 ? (double)sentBytes / totalBytes : 1.0);
            }

            if (totalBytes == 0)
            {
                await SendAsync(new MediaDataMessage(BinaryData.Empty, IsFinal: true), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        await SendMediaAsync(
            new MediaCommandMessage(MediaAction.Load, Url: "", title, mimeType, durationMs, startPositionMs),
            cancellationToken).ConfigureAwait(false);
        return await playbackTask.ConfigureAwait(false);
    }

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

    /// <summary>Waits until the receiver either starts, ends, or rejects the current media item.</summary>
    public async Task<PlaybackStateMessage> WaitForPlaybackStartAsync(CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<PlaybackStateMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        void OnPlaybackState(PlaybackStateMessage message)
        {
            if (message.State is PlaybackState.Playing or PlaybackState.Error or PlaybackState.Ended)
            {
                completion.TrySetResult(message);
            }
        }

        PlaybackStateReceived += OnPlaybackState;
        try
        {
            var closedTask = closed.Task.WaitAsync(cancellationToken);
            await Task.WhenAny(completion.Task, closedTask).ConfigureAwait(false);

            // A final playback state and EOF can arrive in the same receive-loop turn. Prefer the
            // state that the receiver deliberately sent over the transport closing immediately
            // afterward; choosing EOF here made short and empty media fail nondeterministically.
            if (completion.Task.IsCompleted)
            {
                return await completion.Task.ConfigureAwait(false);
            }

            var exception = await closedTask.ConfigureAwait(false);
            if (completion.Task.IsCompleted)
            {
                return await completion.Task.ConfigureAwait(false);
            }

            throw new IOException("The receiver session ended before playback started.", exception);
        }
        finally
        {
            PlaybackStateReceived -= OnPlaybackState;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        isConnected = false;
        receiveCancellationTokenSource.Cancel();
        await sendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Let an already-started frame finish before closing the stream. New senders observe
            // disposeStarted either before or after entering the gate and fail without writing.
            await stream.DisposeAsync().ConfigureAwait(false);
            client.Dispose();
        }
        finally
        {
            sendGate.Release();
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
            sendGate.Dispose();
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

                if (frame.Message is ByeMessage)
                {
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
    /// counter on the host stayed perfectly healthy — sixty frames, 1.5 MB, zero recoveries — while
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
    /// order of preference — never simply the best thing the receiver claims. A receiver's
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
