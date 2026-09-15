using System.Net;
using System.Net.Sockets;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

public sealed class CastSessionTests
{
    [Fact]
    public void TryReadBrowserPortHint_SessionTokenAckWithDecimalPort_ReturnsPort()
    {
        var hint = CastSession.TryReadBrowserPortHint(new AuthMessage(
            AuthMethod.SessionToken,
            BinaryData.From("accepted"u8),
            PublicKeyFingerprint: "34915"));

        hint.ShouldBe(34915);
    }

    [Fact]
    public void TryReadBrowserPortHint_IgnoresNonPortFingerprints()
    {
        CastSession.TryReadBrowserPortHint(new AuthMessage(
            AuthMethod.SessionToken,
            BinaryData.From("accepted"u8),
            PublicKeyFingerprint: "AB12-CD34")).ShouldBeNull();
    }

    [Fact]
    public async Task ConnectAsync_AuthAckWithBrowserPortHint_ExposesPortOnSession()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            _ = await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(1, new HelloMessage(
                1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
            _ = await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(1, new AuthMessage(
                AuthMethod.SessionToken,
                BinaryData.From("accepted"u8),
                PublicKeyFingerprint: "34894")));
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);

        session.BrowserSecureEndpointPort.ShouldBe(34894);
        await server;
    }

    [Fact]
    public async Task ConnectAsync_V1PeerKeepsAllOrdinaryTrafficAtNegotiatedV1()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            var hostHello = await ReadFrameAsync(stream);
            hostHello.ProtocolVersion.ShouldBe(ProtocolVersion.MinSupported);
            await WriteFrameAsync(stream, new WireFrame(1, new HelloMessage(
                1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
            var auth = await ReadFrameAsync(stream);
            auth.ProtocolVersion.ShouldBe(1);
            await WriteFrameAsync(stream, new WireFrame(1, new AuthMessage(
                AuthMethod.SessionToken, BinaryData.From("accepted"u8))));
            var control = await ReadFrameAsync(stream);
            control.ProtocolVersion.ShouldBe(1);
            control.Message.ShouldBeOfType<ControlMessage>();
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);
        session.NegotiatedProtocolVersion.ShouldBe(1);
        await session.SendControlAsync(
            new ControlMessage(1, new KeyControl(KeyAction.Down, 23)),
            TestContext.Current.CancellationToken);
        await server;
    }

    [Fact]
    public async Task SendSurfaceAsync_BrowserSurfaceIsRejectedBeforeThePlaintextSessionWritesIt()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                1, 2, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                AuthMethod.SessionToken, BinaryData.From("accepted"u8))));
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            session.SendSurfaceAsync(new SurfaceMessage(SurfaceMode.Browser)));
        await server;
    }

    [Fact]
    public async Task ConnectAsync_CompletesHelloAndAuthAndSelectsACodecThisHostCanEncode()
    {
        // This test used to be called "...AndSelectsH265", and asserted exactly the bug: the host
        // promised HEVC to any receiver that could decode it while the engine only ever produces
        // H.264. The receiver built the wrong decoder, the television stayed on its pairing screen,
        // and the host reported sixty frames sent and zero errors. A test that pins the wrong
        // answer is worse than no test, because it makes the right answer look like a regression.

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(
                TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            var hello = await ReadFrameAsync(stream);
            hello.Message.ShouldBeOfType<HelloMessage>();
            await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                1,
                1,
                "Fire TV",
                ValueList<CodecId>.From([CodecId.H265, CodecId.H264]),
                1920,
                1080,
                320)));
            var auth = await ReadFrameAsync(stream);
            auth.Message.ShouldBeOfType<AuthMessage>();
            await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                AuthMethod.SessionToken,
                BinaryData.From("accepted"u8))));
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback,
            port,
            "123456",
            cancellationToken: TestContext.Current.CancellationToken);

        session.VideoCodec.ShouldBe(CodecId.H264);
        session.LocalAddress.ShouldBe(IPAddress.Loopback);
        session.PeerHello.Message.ShouldBeOfType<HelloMessage>().DeviceName.ShouldBe("Fire TV");
        await server;
    }

    [Fact]
    public async Task SendVideoAsync_SendsConfigurationAndFirstKeyFrame()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                AuthMethod.SessionToken, BinaryData.From("accepted"u8))));
            var config = await ReadFrameAsync(stream);
            var video = await ReadFrameAsync(stream);
            config.Message.ShouldBeOfType<VideoConfigMessage>().Codec.ShouldBe(CodecId.H264);
            video.Message.ShouldBeOfType<VideoPacket>().KeyFrame.ShouldBeTrue();
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);
        await session.SendVideoConfigAsync(
            CodecId.H264,
            1280,
            720,
            [],
            TestContext.Current.CancellationToken);
        await session.SendVideoAsync(0, true, BinaryData.From([0, 0, 1, 0x65]), TestContext.Current.CancellationToken);
        await server;
    }

    [Fact]
    public async Task WaitForPlaybackStartAsync_ReceiverReportsPlaying_ReturnsTheReceiverState()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                AuthMethod.SessionToken, BinaryData.From("accepted"u8))));
            (await ReadFrameAsync(stream)).Message.ShouldBeOfType<MediaCommandMessage>();
            await WriteFrameAsync(stream, new WireFrame(new PlaybackStateMessage(
                PlaybackState.Playing, 1_000, 5_000, "Playing on this TV")));
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);
        var state = await session.SendMediaAndWaitForPlaybackStartAsync(
            new MediaCommandMessage(MediaAction.Load, "http://127.0.0.1/video.mp4", "Video", "video/mp4"),
            TestContext.Current.CancellationToken);

        state.State.ShouldBe(PlaybackState.Playing);
        state.Detail.ShouldBe("Playing on this TV");
        await server;
    }

    [Fact]
    public async Task PushMediaAndWaitForPlaybackStartAsync_SendsExactBytesThenAnEmptyUrlLoad()
    {
        // The receiver plays a file it fetched itself over HTTP whenever a real URL works, but this
        // path exists for the opposite case: some Fire OS builds silently drop an outbound
        // connection the receiver app initiates to a private LAN address, even though the same
        // address works fine for this connection (host to receiver). Pushing the bytes over the
        // already-open connection sidesteps that restriction, so the receiver must see the exact
        // original bytes reassembled from the chunks, followed by a load with no URL.
        // Bigger than one 512 KB push chunk (see CastSession.PushChunkBytes), so this exercises the
        // multi-chunk loop and reassembly rather than the degenerate single-chunk case.
        var originalBytes = System.Text.Encoding.UTF8.GetBytes(new string('x', 1_300_000));
        var filePath = Path.GetTempFileName();
        await File.WriteAllBytesAsync(filePath, originalBytes, TestContext.Current.CancellationToken);
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                var stream = client.GetStream();
                await ReadFrameAsync(stream);
                await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                    1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
                await ReadFrameAsync(stream);
                await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                    AuthMethod.SessionToken, BinaryData.From("accepted"u8))));

                using var received = new MemoryStream();
                MediaDataMessage chunk;
                do
                {
                    chunk = (await ReadFrameAsync(stream)).Message.ShouldBeOfType<MediaDataMessage>();
                    received.Write([.. chunk.Data]);
                } while (!chunk.IsFinal);

                received.ToArray().ShouldBe(originalBytes);

                var command = (await ReadFrameAsync(stream)).Message.ShouldBeOfType<MediaCommandMessage>();
                command.Url.ShouldBe(string.Empty);
                command.Title.ShouldBe("clip.mp4");
                command.MimeType.ShouldBe("video/mp4");

                await WriteFrameAsync(stream, new WireFrame(new PlaybackStateMessage(
                    PlaybackState.Playing, 0, -1, "Playing on this TV")));
            }, TestContext.Current.CancellationToken);

            await using var session = await CastSession.ConnectAsync(
                IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);
            // A plain Progress<T> marshals through SynchronizationContext.Post, which does not
            // guarantee delivery before the awaited call returns — reporting straight into the list
            // keeps this assertion deterministic instead of racing the last callback's delivery.
            var reported = new List<double>();
            var state = await session.PushMediaAndWaitForPlaybackStartAsync(
                filePath,
                "clip.mp4",
                "video/mp4",
                progress: new SynchronousProgress<double>(reported.Add),
                cancellationToken: TestContext.Current.CancellationToken);

            state.State.ShouldBe(PlaybackState.Playing);
            reported.ShouldNotBeEmpty();
            reported[^1].ShouldBe(1.0);
            await server;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task PushMediaAndWaitForPlaybackStartAsync_EmptyFile_StillSendsOneFinalChunk()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
                var stream = client.GetStream();
                await ReadFrameAsync(stream);
                await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                    1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
                await ReadFrameAsync(stream);
                await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                    AuthMethod.SessionToken, BinaryData.From("accepted"u8))));

                var chunk = (await ReadFrameAsync(stream)).Message.ShouldBeOfType<MediaDataMessage>();
                chunk.IsFinal.ShouldBeTrue();
                chunk.Data.Length.ShouldBe(0);

                await ReadFrameAsync(stream);
                await WriteFrameAsync(stream, new WireFrame(new PlaybackStateMessage(PlaybackState.Ended)));
            }, TestContext.Current.CancellationToken);

            await using var session = await CastSession.ConnectAsync(
                IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);
            await session.PushMediaAndWaitForPlaybackStartAsync(
                filePath, "empty.mp4", "video/mp4", cancellationToken: TestContext.Current.CancellationToken);
            await server;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task WaitForPlaybackStartAsync_DoesNotLosePlayingImmediatelyAfterBuffering()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                AuthMethod.SessionToken, BinaryData.From("accepted"u8))));
            await ReadFrameAsync(stream);

            // Deliver both states back-to-back. The old waiter unsubscribed after Buffering and
            // could miss Playing in the gap before its next subscription.
            var buffering = WireCodec.Encode(new WireFrame(new PlaybackStateMessage(PlaybackState.Buffering)));
            var playing = WireCodec.Encode(new WireFrame(new PlaybackStateMessage(PlaybackState.Playing)));
            var combined = new byte[buffering.Length + playing.Length];
            buffering.CopyTo(combined, 0);
            playing.CopyTo(combined, buffering.Length);
            await stream.WriteAsync(combined, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);
        var state = await session.SendMediaAndWaitForPlaybackStartAsync(
            new MediaCommandMessage(MediaAction.Load, "http://127.0.0.1/video.mp4", "Video", "video/mp4"),
            TestContext.Current.CancellationToken);

        state.State.ShouldBe(PlaybackState.Playing);
        await server;
    }

    [Fact]
    public async Task ConcurrentSends_RemainCompleteLengthPrefixedFrames()
    {
        const int messageCount = 12;
        const int payloadBytes = 256 * 1024;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var stream = client.GetStream();
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new HelloMessage(
                1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320)));
            await ReadFrameAsync(stream);
            await WriteFrameAsync(stream, new WireFrame(new AuthMessage(
                AuthMethod.SessionToken, BinaryData.From("accepted"u8))));

            var markers = new HashSet<byte>();
            for (var index = 0; index < messageCount; index++)
            {
                var packet = (await ReadFrameAsync(stream)).Message.ShouldBeOfType<VideoPacket>();
                packet.Data.Length.ShouldBe(payloadBytes);
                var bytes = packet.Data.ToArray();
                bytes.ShouldAllBe(value => value == bytes[0]);
                markers.Add(bytes[0]);
            }

            markers.Count.ShouldBe(messageCount);
        }, TestContext.Current.CancellationToken);

        await using var session = await CastSession.ConnectAsync(
            IPAddress.Loopback, port, "123456", cancellationToken: TestContext.Current.CancellationToken);
        var sends = Enumerable.Range(1, messageCount).Select(index =>
        {
            var payload = new byte[payloadBytes];
            Array.Fill(payload, (byte)index);
            return session.SendVideoAsync(
                index,
                keyFrame: index == 1,
                BinaryData.From(payload),
                TestContext.Current.CancellationToken);
        });

        await Task.WhenAll(sends);
        await server;
    }

    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    private static async Task<WireFrame> ReadFrameAsync(NetworkStream stream)
    {
        var length = new byte[4];
        await stream.ReadExactlyAsync(length);
        var bodyLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length);
        var bytes = new byte[bodyLength + 4];
        length.CopyTo(bytes, 0);
        await stream.ReadExactlyAsync(bytes.AsMemory(4));
        return WireCodec.Decode(bytes);
    }

    private static Task WriteFrameAsync(NetworkStream stream, WireFrame frame) =>
        stream.WriteAsync(WireCodec.Encode(frame)).AsTask();
}
/// <summary>
/// Codec negotiation, which decides what the handshake promises the receiver.
/// </summary>
/// <remarks>
/// These pin a bug that cost an afternoon and produced no error anywhere. The host advertised
/// H.265 first and chose it whenever the receiver could decode it, while the engine only ever
/// produces H.264. Every counter on the host stayed healthy — sixty frames, 1.5 MB, zero
/// recoveries — and the television sat on its pairing screen the whole time, because a decoder
/// configured for HEVC can do nothing with AVC.
/// </remarks>
public sealed class CodecNegotiationTests
{
    [Fact]
    public void SelectCodec_WhenTheReceiverOffersBoth_ChoosesTheOneThisHostCanActuallyEncode()
    {
        // The exact regression. A receiver that decodes HEVC is not a reason to promise HEVC.
        var hello = Hello(CodecId.H265, CodecId.H264);

        var chosen = CastSession.SelectCodec(hello);

        chosen.ShouldBe(CodecId.H264);
    }

    [Fact]
    public void SelectCodec_WhenTheReceiverOffersOnlyHevc_RefusesRatherThanPromisingIt()
    {
        // Refusing is the honest outcome: sending AVC to an HEVC decoder gives a blank screen and
        // a host that reports success, which is strictly worse than an error at connection time.
        var hello = Hello(CodecId.H265);

        Should.Throw<WireFormatException>(() => CastSession.SelectCodec(hello));
    }

    [Fact]
    public void SelectCodec_WhenTheReceiverOffersOnlyH264_ChoosesIt()
    {
        CastSession.SelectCodec(Hello(CodecId.H264)).ShouldBe(CodecId.H264);
    }

    [Fact]
    public void SelectCodec_WhenTheReceiverOffersNothing_Refuses()
    {
        Should.Throw<WireFormatException>(() => CastSession.SelectCodec(Hello()));
    }

    [Fact]
    public void SelectCodec_WhenTheReceiverOffersAnUnknownCodec_Refuses()
    {
        // Forward compatibility must not become optimism: a codec this host has never heard of is
        // certainly not one it can encode.
        Should.Throw<WireFormatException>(() => CastSession.SelectCodec(Hello(new CodecId(99))));
    }

    [Fact]
    public void SelectCodec_TheErrorNamesWhatThisHostCanEncode()
    {
        // Whoever reads this failure needs to know which side is short, and of what.
        var failure = Should.Throw<WireFormatException>(
            () => CastSession.SelectCodec(Hello(CodecId.H265)));

        failure.Message.ShouldContain(CodecId.H264.Value.ToString());
    }

    private static HelloMessage Hello(params CodecId[] codecs) => new(
        ProtocolVersion.MinSupported,
        ProtocolVersion.Current,
        "RECEIVER",
        ValueList<CodecId>.From(codecs),
        1920,
        1080,
        96);
}
