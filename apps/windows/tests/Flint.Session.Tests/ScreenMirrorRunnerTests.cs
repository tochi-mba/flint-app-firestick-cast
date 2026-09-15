using Flint.Core;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>
/// The mirror loop is where a working engine and a working receiver can still add up to a black
/// television, so these assert on ordering and on what reaches the wire — not just on whether the
/// loop finished.
/// </summary>
public sealed class ScreenMirrorRunnerTests
{
    /// <summary>Bytes the fake engine reports for each access unit.</summary>
    private const int AccessUnitBytes = 64;

    [Fact]
    public async Task RunAsync_AnnouncesTheMirrorSurfaceBeforeAnyVideo()
    {
        // A receiver still on its idle surface renders nothing, no matter how good the stream is.

        // Arrange
        using var harness = Harness.WithFrames(1);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Log[0].ShouldBe("surface:Mirror");
    }

    [Fact]
    public async Task RunAsync_SendsTheDecoderConfigurationBeforeTheFirstAccessUnit()
    {
        // The ordering that matters most: an access unit that arrives before the configuration has
        // nothing to be decoded against, and the receiver reports a healthy session while showing
        // black.

        // Arrange
        using var harness = Harness.WithFrames(3);

        // Act
        await harness.RunAsync();

        // Assert
        var configIndex = harness.Transport.Log.FindIndex(entry => entry.StartsWith("config:", StringComparison.Ordinal));
        var firstVideoIndex = harness.Transport.Log.FindIndex(entry => entry.StartsWith("video:", StringComparison.Ordinal));
        configIndex.ShouldBeGreaterThanOrEqualTo(0);
        firstVideoIndex.ShouldBeGreaterThan(configIndex);
    }

    [Fact]
    public async Task RunAsync_SendsTheEncodedSizeRatherThanTheDesktopSize()
    {
        // The engine may downscale a 4K desktop to the television's resolution. Telling the
        // receiver the desktop's size instead would configure its decoder for frames it will
        // never be sent.

        // Arrange
        using var harness = Harness.WithFrames(1);
        harness.Session.Width = 1920;
        harness.Session.Height = 1200;

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Log.ShouldContain("config:1920x1200:2");
    }

    [Fact]
    public async Task RunAsync_AnnouncesTheCodecTheEngineEncodedRatherThanTheOneTheHandshakeNegotiated()
    {
        // Caught on a Fire TV Stick 4K: the handshake settled on HEVC, the engine encodes H.264,
        // and the decoder configuration carried the negotiated codec. The television built an
        // OMX.MTK.VIDEO.DECODER.HEVC, failed to initialise it, and showed "SCREEN MIRROR STOPPED"
        // while the host cheerfully reported hundreds of frames sent.

        // Arrange
        using var harness = Harness.WithFrames(1);
        harness.Session.Codec = VideoCodec.H264;

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.AnnouncedCodec.ShouldBe(CodecId.H264);
    }

    [Fact]
    public async Task RunAsync_AnEngineThatEncodedHevcIsAnnouncedAsHevc()
    {
        // The mirror is H.264 today, so the guard above would also pass if the codec were simply
        // hardcoded. This is what proves it is read from the engine.

        // Arrange
        using var harness = Harness.WithFrames(1);
        harness.Session.Codec = VideoCodec.H265;

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.AnnouncedCodec.ShouldBe(CodecId.H265);
    }

    [Fact]
    public async Task RunAsync_ForwardsEveryEncodedAccessUnitWithItsOwnTimestampAndFlag()
    {
        // Arrange
        using var harness = Harness.WithFrames(3);

        // Act
        await harness.RunAsync();

        // Assert: the first is a key frame, the rest are not, and each carries its own time.
        harness.Transport.Video.Count.ShouldBe(3);
        harness.Transport.Video[0].KeyFrame.ShouldBeTrue();
        harness.Transport.Video[1].KeyFrame.ShouldBeFalse();
        harness.Transport.Video.Select(frame => frame.PresentationTimeUs).ShouldBe([0L, 33_333L, 66_666L]);
    }

    [Fact]
    public async Task RunAsync_SendsExactlyTheEncodedBytesAndNotTheWholeBuffer()
    {
        // The frame buffer is rented once and reused, so it is far larger than any one access
        // unit. Sending its full length would push megabytes of stale bytes per frame.

        // Arrange
        using var harness = Harness.WithFrames(1);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Video[0].Data.Count.ShouldBe(AccessUnitBytes);
    }

    [Fact]
    public async Task RunAsync_SendsTheEncodersBytesRatherThanWhateverWasInTheBuffer()
    {
        // Proves the runner reads back what the engine wrote instead of forwarding an untouched
        // rented buffer, which would look like a working mirror and decode to nothing.

        // Arrange
        using var harness = Harness.WithFrames(1);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Video[0].Data.ShouldAllBe(value => value == FakeSession.FillByte);
    }

    [Fact]
    public async Task RunAsync_TicksThatProducedNothingAreNotSentAsEmptyFrames()
    {
        // A still desktop produces these continuously. Forwarding them would spend bandwidth to
        // tell the receiver nothing changed, and an empty access unit fails decode outright.

        // Arrange
        using var harness = Harness.WithScript([
            MirrorTick.Nothing,
            new MirrorTick(MirrorTickKind.Encoded, AccessUnitBytes, true, 0),
            MirrorTick.Nothing,
        ]);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Video.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_ARecoveredTickIsNotSentAsAFrameEither()
    {
        // A lock screen arrives as a recovery. It carries no pixels, and the engine already forces
        // the next real frame to be a key frame, so there is nothing for the wire here.

        // Arrange
        using var harness = Harness.WithScript([
            new MirrorTick(MirrorTickKind.Recovered, 0, false, 0),
            new MirrorTick(MirrorTickKind.Encoded, AccessUnitBytes, true, 0),
        ]);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Video.Count.ShouldBe(1);
        harness.Transport.Video[0].KeyFrame.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_ReturnsTheReceiverToItsIdleSurfaceWhenTheMirrorStops()
    {
        // Otherwise the television sits on a mirror surface waiting for frames that stopped
        // coming, which looks identical to a crash.

        // Arrange
        using var harness = Harness.WithFrames(1);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Log[^1].ShouldBe("surface:Idle");
    }

    [Fact]
    public async Task RunAsync_WhenTheReceiverIsAlreadyGone_DoesNotTryToResetItsSurface()
    {
        // Arrange
        using var harness = Harness.WithFrames(5, disconnectAfterVideo: 1);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Log.ShouldNotContain("surface:Idle");
    }

    [Fact]
    public async Task RunAsync_StopsWhenTheReceiverSessionEnds()
    {
        // The engine would happily keep encoding forever. Nobody is listening.

        // Arrange
        using var harness = Harness.WithFrames(50, disconnectAfterVideo: 2);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Transport.Video.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RunAsync_CancellationEndsTheMirrorWithoutThrowing()
    {
        // Stopping is how a mirror ends, so it must read as a normal finish rather than a fault.

        // Arrange
        using var harness = Harness.WithFrames(500);
        harness.Session.OnTick = index =>
        {
            if (index == 3)
            {
                harness.Stop();
            }
        };

        // Act
        var stats = await harness.RunAsync();

        // Assert: it stopped promptly and reported real work, rather than throwing or running the
        // whole script. The exact count is the tick already in flight when the stop arrived, which
        // is not a promise worth pinning.
        stats.FramesEncoded.ShouldBeInRange(3, 5);
    }

    [Fact]
    public async Task RunAsync_DisposesTheEngineSessionSoTheCaptureIsReleased()
    {
        // Desktop duplication is exclusive. A session left open means no other program — including
        // the next Flint mirror — can capture the screen.

        // Arrange
        using var harness = Harness.WithFrames(1);

        // Act
        await harness.RunAsync();

        // Assert
        harness.Session.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_KeepsEveryNativeLifecycleCallOnOneDedicatedThread()
    {
        // Media Foundation balances COM initialisation when the encoder is destroyed. Starting on
        // the caller and resuming Next or Dispose on an async continuation thread is therefore a
        // process-level lifetime bug, not merely a performance detail.

        // Arrange
        using var harness = Harness.WithFrames(3);
        var callerThread = Environment.CurrentManagedThreadId;

        // Act
        await harness.RunAsync();

        // Assert
        var nativeThreads = new[] { harness.Engine.StartThreadId!.Value }
            .Concat(harness.Session.LifecycleThreadIds)
            .Distinct()
            .ToArray();
        nativeThreads.Length.ShouldBe(1);
        nativeThreads[0].ShouldNotBe(callerThread);
        harness.Engine.StartedOnThreadPool.ShouldBeFalse();
        harness.Session.LifecycleThreadPoolFlags.ShouldAllBe(value => !value);
    }

    [Fact]
    public async Task RunAsync_AFrameFailureReturnsAnAnnouncedReceiverSurfaceToIdle()
    {
        // Once Mirror has been announced, leaving it selected after the encoder fails strands the
        // television on a black surface even though the control connection is still healthy.

        // Arrange
        using var harness = Harness.WithFrames(1);
        harness.Session.FailOnNext = true;

        // Act
        await Should.ThrowAsync<MirrorEngineException>(() => harness.RunAsync());

        // Assert
        harness.Transport.Log[^1].ShouldBe("surface:Idle");
        harness.Session.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_DisposesTheEngineSessionEvenWhenTheTransportFails()
    {
        // A television unplugged mid-mirror must not leave the screen capture held open.

        // Arrange
        using var harness = Harness.WithFrames(3);
        harness.Transport.FailOnVideo = true;

        // Act
        await Should.ThrowAsync<IOException>(() => harness.RunAsync());

        // Assert
        harness.Session.IsDisposed.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenTheEngineCannotStart_TheReceiverIsNeverToldToExpectAStream()
    {
        // Announcing a mirror and then failing to produce one is exactly the black-screen bug this
        // path exists to avoid.

        // Arrange
        var transport = new RecordingTransport();
        var runner = new ScreenMirrorRunner(new FailingEngine());

        // Act
        var act = () => runner.RunAsync(transport, new MirrorSessionOptions());

        // Assert
        await act.ShouldThrowAsync<MirrorEngineException>();
        transport.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_PassesTheCallersOptionsToTheEngineUnchanged()
    {
        // Arrange
        using var harness = Harness.WithFrames(1);
        var options = new MirrorSessionOptions(
            OutputIndex: 2,
            FrameRate: 60,
            BitrateBitsPerSecond: 20_000_000,
            MaxWidth: 1280);

        // Act
        await harness.RunAsync(options);

        // Assert
        harness.Engine.Requested.ShouldBe(options);
    }

    [Fact]
    public async Task RunAsync_UsesTheCallersCaptionSoTheTelevisionSaysWhatItIsWaitingFor()
    {
        // Arrange
        using var harness = Harness.WithFrames(1);

        // Act
        await harness.RunAsync(caption: "Tochi's PC");

        // Assert
        harness.Transport.Captions.ShouldContain("Tochi's PC");
    }

    [Fact]
    public async Task RunAsync_ReportsCountersAsFramesGoOutSoDiagnosticsCanFollowALiveMirror()
    {
        // Arrange
        using var harness = Harness.WithFrames(3);
        var reports = new List<MirrorSessionStats>();
        harness.Runner.StatsUpdated += reports.Add;

        // Act
        await harness.RunAsync();

        // Assert
        reports.Count.ShouldBe(3);
        reports[^1].FramesEncoded.ShouldBe(3);
    }

    [Fact]
    public async Task RunAsync_ReturnsTheFinalCountersRatherThanAnEmptyReport()
    {
        // Arrange
        using var harness = Harness.WithFrames(4);

        // Act
        var stats = await harness.RunAsync();

        // Assert
        stats.FramesEncoded.ShouldBe(4);
        stats.BytesEncoded.ShouldBe(4L * AccessUnitBytes);
    }

    [Fact]
    public async Task RunAsync_RequestsAnIdrWhenTheReceiverReportsNewDroppedFrames()
    {
        using var harness = Harness.WithFrames(4);
        harness.Transport.ReportDropAfterVideo = 1;

        await harness.RunAsync();

        harness.Session.KeyFrameRequests.ShouldBe(1);
    }

    [Fact]
    public void Constructor_WithoutAnEngine_FailsImmediatelyRatherThanOnTheFirstFrame()
    {
        Should.Throw<ArgumentNullException>(() => new ScreenMirrorRunner(null!));
    }

    [Fact]
    public async Task RunAsync_WithoutATransport_FailsBeforeTouchingTheEngine()
    {
        // Arrange
        using var harness = Harness.WithFrames(1);

        // Act
        var act = () => harness.Runner.RunAsync(null!, new MirrorSessionOptions());

        // Assert
        await act.ShouldThrowAsync<ArgumentNullException>();
        harness.Engine.Requested.ShouldBeNull();
    }

    /// <summary>
    /// A runner wired to a scripted engine and a recording transport.
    /// </summary>
    /// <remarks>
    /// The runner loops until told to stop, so the fake session cancels this harness's own token
    /// once its script is exhausted. Each harness owns its token, which keeps these tests
    /// independent of one another and of the order they run in.
    /// </remarks>
    private sealed class Harness : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();

        private Harness(FakeSession session, int disconnectAfterVideo)
        {
            Session = session;
            session.OnScriptExhausted = cancellation.Cancel;
            Engine = new FakeEngine(session);
            Runner = new ScreenMirrorRunner(Engine);
            Transport = new RecordingTransport { DisconnectAfterVideo = disconnectAfterVideo };
        }

        internal FakeSession Session { get; }

        internal FakeEngine Engine { get; }

        internal ScreenMirrorRunner Runner { get; }

        internal RecordingTransport Transport { get; }

        internal static Harness WithFrames(int count, int disconnectAfterVideo = int.MaxValue)
        {
            var ticks = new List<MirrorTick>(count);
            for (var index = 0; index < count; index++)
            {
                ticks.Add(new MirrorTick(MirrorTickKind.Encoded, AccessUnitBytes, index == 0, index * 33_333L));
            }

            return new Harness(new FakeSession(ticks), disconnectAfterVideo);
        }

        internal static Harness WithScript(IReadOnlyList<MirrorTick> script) =>
            new(new FakeSession(script), int.MaxValue);

        internal void Stop() => cancellation.Cancel();

        internal Task<MirrorSessionStats> RunAsync(
            MirrorSessionOptions? options = null,
            string caption = "Screen mirror") =>
            Runner.RunAsync(Transport, options ?? new MirrorSessionOptions(), caption, cancellation.Token);

        public void Dispose() => cancellation.Dispose();
    }

    private sealed class FakeEngine(FakeSession session) : IMirrorEngine
    {
        internal MirrorSessionOptions? Requested { get; private set; }

        internal int? StartThreadId { get; private set; }

        internal bool StartedOnThreadPool { get; private set; }

        public IMirrorEngineSession Start(MirrorSessionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            Requested = options;
            StartThreadId = Environment.CurrentManagedThreadId;
            StartedOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
            return session;
        }
    }

    private sealed class FailingEngine : IMirrorEngine
    {
        public IMirrorEngineSession Start(MirrorSessionOptions options) =>
            throw new MirrorEngineException("no encoder on this machine");
    }

    private sealed class FakeSession(IReadOnlyList<MirrorTick> script) : IMirrorEngineSession
    {
        /// <summary>What the fake engine writes into the frame buffer.</summary>
        internal const byte FillByte = 0xAB;

        private int at;
        private long framesEncoded;
        private long bytesEncoded;

        internal List<int> LifecycleThreadIds { get; } = [];

        internal List<bool> LifecycleThreadPoolFlags { get; } = [];

        internal Action<int>? OnTick { get; set; }

        internal Action? OnScriptExhausted { get; set; }

        internal bool IsDisposed { get; private set; }

        internal bool FailOnNext { get; set; }

        internal int KeyFrameRequests { get; private set; }

        public VideoCodec Codec { get; set; } = VideoCodec.H264;

        public int Width { get; set; } = 1280;

        public int Height { get; set; } = 720;

        public IReadOnlyList<byte[]> CodecSpecificData { get; } =
            [[0, 0, 0, 1, 0x67], [0, 0, 0, 1, 0x68]];

        public MirrorTick Next(Span<byte> buffer)
        {
            RecordLifecycleThread();
            if (FailOnNext)
            {
                throw new MirrorEngineException("the encoder refused the frame");
            }

            OnTick?.Invoke(at);

            if (at >= script.Count)
            {
                // Nothing left to replay. Ending the loop here keeps each test's assertions about
                // what was sent rather than about how long the loop ran.
                OnScriptExhausted?.Invoke();
                return MirrorTick.Nothing;
            }

            var tick = script[at++];
            if (tick.Kind is MirrorTickKind.Encoded)
            {
                buffer[..tick.ByteCount].Fill(FillByte);
                framesEncoded++;
                bytesEncoded += tick.ByteCount;
            }

            return tick;
        }

        public void RequestKeyFrame()
        {
            RecordLifecycleThread();
            KeyFrameRequests++;
        }

        public MirrorSessionStats ReadStats()
        {
            RecordLifecycleThread();
            return new MirrorSessionStats(framesEncoded, at - framesEncoded, 0, bytesEncoded);
        }

        public void Dispose()
        {
            RecordLifecycleThread();
            IsDisposed = true;
        }

        private void RecordLifecycleThread()
        {
            LifecycleThreadIds.Add(Environment.CurrentManagedThreadId);
            LifecycleThreadPoolFlags.Add(Thread.CurrentThread.IsThreadPoolThread);
        }
    }

    private sealed class RecordingTransport : IMirrorTransport, IMirrorFeedbackTransport
    {
        internal List<string> Log { get; } = [];

        internal List<VideoPacket> Video { get; } = [];

        internal List<string> Captions { get; } = [];

        internal int DisconnectAfterVideo { get; init; } = int.MaxValue;

        internal bool FailOnVideo { get; set; }

        internal int ReportDropAfterVideo { get; set; } = int.MaxValue;

        public event Action<StatsMessage>? StatsReceived;

        public bool IsConnected => Video.Count < DisconnectAfterVideo;

        public Task SendSurfaceAsync(SurfaceMessage surface, CancellationToken cancellationToken = default)
        {
            Log.Add($"surface:{surface.Mode}");
            Captions.Add(surface.Caption);
            return Task.CompletedTask;
        }

        internal CodecId? AnnouncedCodec { get; private set; }

        public Task SendVideoConfigAsync(
            CodecId codec,
            int width,
            int height,
            IEnumerable<BinaryData> codecSpecificData,
            CancellationToken cancellationToken = default)
        {
            AnnouncedCodec = codec;
            Log.Add($"config:{width}x{height}:{codecSpecificData.Count()}");
            return Task.CompletedTask;
        }

        public Task SendVideoAsync(
            long presentationTimeUs,
            bool keyFrame,
            BinaryData data,
            CancellationToken cancellationToken = default)
        {
            if (FailOnVideo)
            {
                throw new IOException("the receiver went away");
            }

            Log.Add($"video:{presentationTimeUs}:{keyFrame}:{data.Count}");
            Video.Add(new VideoPacket(presentationTimeUs, keyFrame, data));
            if (Video.Count == ReportDropAfterVideo)
            {
                StatsReceived?.Invoke(new StatsMessage(0, 0, 0, 1));
            }
            return Task.CompletedTask;
        }
    }
}
