using System.Buffers;
using Flint.Core;
using Flint.Protocol;

namespace Flint.Session;

/// <summary>
/// Drives a capture-and-encode session onto a receiver session.
/// </summary>
/// <remarks>
/// <para>
/// The engine decides everything that happens between two frames; this only drains what the engine
/// has already produced and puts it on the wire, which is the whole reason the managed side is
/// allowed anywhere near a mirror at all.
/// </para>
/// <para>
/// Order matters and is not cosmetic. The surface is announced first so the television stops
/// showing its idle screen, then the decoder configuration, then access units — a receiver that
/// gets an access unit before its configuration has nothing to decode it against and shows black
/// while reporting a healthy session.
/// </para>
/// </remarks>
/// <param name="engine">Starts the capture-and-encode session.</param>
public sealed class ScreenMirrorRunner(IMirrorEngine engine)
{
    /// <summary>
    /// The frame buffer size, large enough for a key frame at the resolutions this path encodes.
    /// </summary>
    /// <remarks>
    /// Rented once for the whole session rather than per frame: a per-frame allocation of this size
    /// is the single most expensive thing that could be added to the loop.
    /// </remarks>
    public const int FrameBufferBytes = 4 * 1024 * 1024;

    /// <summary>How long an idle tick waits before asking the engine again.</summary>
    /// <remarks>
    /// The engine's own tick already blocks waiting for the desktop to change, so this is only a
    /// courtesy yield. Zero would spin a core against a still desktop for nothing.
    /// </remarks>
    private static readonly TimeSpan IdlePause = TimeSpan.FromMilliseconds(4);

    private readonly IMirrorEngine engine = engine ?? throw new ArgumentNullException(nameof(engine));

    /// <summary>Raised after each tick that changed the counters, for the diagnostics view.</summary>
    public event Action<MirrorSessionStats>? StatsUpdated;

    /// <summary>
    /// Mirrors the screen until cancelled or until the receiver session ends.
    /// </summary>
    /// <param name="transport">The connected receiver session.</param>
    /// <param name="options">What the session should produce.</param>
    /// <param name="caption">What the television shows while it waits for the first frame.</param>
    /// <param name="cancellationToken">Stops the mirror.</param>
    /// <returns>The counters as they stood when the mirror stopped.</returns>
    /// <exception cref="MirrorEngineException">The host could not capture or encode.</exception>
    public Task<MirrorSessionStats> RunAsync(
        IMirrorTransport transport,
        MirrorSessionOptions options,
        string caption = "Screen mirror",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);

        // A native mirror session owns COM, Media Foundation and DXGI objects. In particular, the
        // encoder balances CoInitializeEx with CoUninitialize when it is destroyed, and those calls
        // must happen on the same OS thread. An async loop cannot promise that: every network await
        // is allowed to resume on another pool thread. Keep the complete native lifetime inside one
        // synchronous, long-running worker instead. Blocking that one worker on socket writes is
        // intentional back-pressure and does not block the UI thread.
        return Task.Factory.StartNew(
            () => RunOnWorker(transport, options, caption, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    /// <summary>Runs the complete native session lifetime on the current worker thread.</summary>
    private MirrorSessionStats RunOnWorker(
        IMirrorTransport transport,
        MirrorSessionOptions options,
        string caption,
        CancellationToken cancellationToken)
    {
        using var session = engine.Start(options);
        byte[]? buffer = null;
        var mirrorSurfaceSelected = false;
        var requestKeyFrame = 0;
        long lastDroppedFrames = 0;
        var feedback = transport as IMirrorFeedbackTransport;
        void OnReceiverStats(StatsMessage stats)
        {
            var previous = Interlocked.Exchange(ref lastDroppedFrames, stats.DroppedVideoFrames);
            if (stats.DroppedVideoFrames > previous)
            {
                Volatile.Write(ref requestKeyFrame, 1);
            }
        }

        if (feedback is not null)
        {
            feedback.StatsReceived += OnReceiverStats;
        }

        try
        {
            transport.SendSurfaceAsync(
                    new SurfaceMessage(SurfaceMode.Mirror, caption),
                    cancellationToken)
                .GetAwaiter()
                .GetResult();
            mirrorSurfaceSelected = true;

            transport.SendVideoConfigAsync(
                    new CodecId((int)session.Codec),
                    session.Width,
                    session.Height,
                    session.CodecSpecificData.Select(block => BinaryData.From(block.AsSpan())),
                    cancellationToken)
                .GetAwaiter()
                .GetResult();

            buffer = ArrayPool<byte>.Shared.Rent(FrameBufferBytes);
            while (!cancellationToken.IsCancellationRequested && transport.IsConnected)
            {
                if (Interlocked.Exchange(ref requestKeyFrame, 0) != 0)
                {
                    // Native state remains thread-affine: feedback only flips an atomic flag on
                    // the receive loop; the dedicated mirror worker performs the ABI call here.
                    session.RequestKeyFrame();
                }

                var tick = session.Next(buffer);

                if (tick.Kind is not MirrorTickKind.Encoded)
                {
                    // The native tick already waited for a desktop change. This short, cancellable
                    // wait is only a courtesy yield for sources that report an idle tick
                    // immediately, and unlike Task.Delay it does not move the native lifetime to a
                    // different continuation thread.
                    if (cancellationToken.WaitHandle.WaitOne(IdlePause))
                    {
                        break;
                    }
                    continue;
                }

                transport.SendVideoAsync(
                        tick.PresentationTimeUs,
                        tick.KeyFrame,
                        BinaryData.From(buffer.AsSpan(0, tick.ByteCount)),
                        cancellationToken)
                    .GetAwaiter()
                    .GetResult();

                StatsUpdated?.Invoke(session.ReadStats());
            }

            return session.ReadStats();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping is how a mirror ends. Reporting the final counters is more useful than
            // propagating the cancellation the caller asked for.
            return session.ReadStats();
        }
        finally
        {
            if (feedback is not null)
            {
                feedback.StatsReceived -= OnReceiverStats;
            }

            if (buffer is not null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // Returning the television to its idle surface is part of every stop, including an
            // encoder failure after the mirror surface was selected. Skipped when the session is
            // already gone, because there is nobody left to tell.
            if (mirrorSurfaceSelected && transport.IsConnected)
            {
                try
                {
                    transport.SendSurfaceAsync(
                            new SurfaceMessage(SurfaceMode.Idle),
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    // The receiver went away as the mirror stopped, which is not a failure of the mirror.
                }
            }
        }
    }
}
