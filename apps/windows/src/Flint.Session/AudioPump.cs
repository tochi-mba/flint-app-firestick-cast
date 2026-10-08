using Flint.Core;
using Flint.Protocol;

namespace Flint.Session;

/// <summary>What a sound share is doing, as the pump last read it.</summary>
/// <param name="Stats">The engine's counters and state.</param>
/// <param name="Problem">Why sound is unavailable, in the engine's words, or null when it is not.</param>
public sealed record AudioPumpReport(AudioShareStats Stats, string? Problem);

/// <summary>How a sound share ended.</summary>
/// <param name="Stats">The counters as they stood at the end.</param>
/// <param name="Problem">Why sound stopped or never started, or null when it was simply stopped.</param>
/// <param name="Failure">Why it could not start, when that is why it ended.</param>
public sealed record AudioPumpEnd(AudioShareStats Stats, string? Problem, AudioStartFailure Failure = AudioStartFailure.None);

/// <summary>Carries this PC's sound to the TV beside a screen share.</summary>
/// <remarks>
/// <para>
/// Like <see cref="ScreenMirrorRunner"/>, this only drains what the engine has produced and puts it
/// on the wire. It runs on its own worker so that nothing about sound, from a slow start to a
/// failure, can hold up or stop the picture: every failure here ends as an
/// <see cref="AudioPumpEnd"/> rather than an exception.
/// </para>
/// <para>
/// The decoder configuration goes first, as it does for the picture: a TV that gets a sound packet
/// before its configuration has nothing to decode it with.
/// </para>
/// </remarks>
/// <param name="engine">Starts the capture-and-encode session.</param>
/// <param name="time">The clock reports are paced by.</param>
public sealed class AudioPump(IAudioEngine engine, TimeProvider time)
{
    /// <summary>Larger than any packet the engine queues.</summary>
    public const int PacketBufferBytes = 4096;

    /// <summary>
    /// How long one wait for a packet lasts, which bounds how quickly a stop, a pause or a change of
    /// delay takes effect.
    /// </summary>
    internal static readonly TimeSpan PacketWait = TimeSpan.FromMilliseconds(100);

    /// <summary>How often the state and level are reported: often enough for a level meter.</summary>
    internal static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(250);

    /// <summary>How long sound waits for a picture starting at the same moment to start its clock.</summary>
    internal static readonly TimeSpan PictureClockWait = TimeSpan.FromSeconds(2);

    private readonly IAudioEngine engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly TimeProvider time = time ?? throw new ArgumentNullException(nameof(time));
    private int delayMilliseconds = -1;

    /// <summary>Creates a pump on the system clock.</summary>
    public AudioPump(IAudioEngine engine)
        : this(engine, TimeProvider.System)
    {
    }

    /// <summary>Raised on the pump's worker about four times a second while sound is shared.</summary>
    public event Action<AudioPumpReport>? Reported;

    /// <summary>Holds sound back by <paramref name="milliseconds"/> from now on, in a running share.</summary>
    public void SetDelay(int milliseconds) => Volatile.Write(ref delayMilliseconds, Math.Max(0, milliseconds));

    /// <summary>Shares sound until cancelled or until the receiver session ends.</summary>
    /// <param name="transport">The connected receiver session.</param>
    /// <param name="options">What to capture and how.</param>
    /// <param name="control">The screen share's control: its pause, and the picture's clock.</param>
    /// <param name="cancellationToken">Stops sharing sound.</param>
    /// <returns>How the share ended. Never faults.</returns>
    public Task<AudioPumpEnd> RunAsync(
        IAudioTransport transport,
        AudioShareOptions options,
        MirrorControl? control = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);

        // A wait for the next packet blocks, so it gets a thread of its own rather than a pool
        // thread that the rest of the app is counting on.
        return Task.Factory.StartNew(
            () => RunOnWorker(transport, options, control, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    private AudioPumpEnd RunOnWorker(
        IAudioTransport transport,
        AudioShareOptions options,
        MirrorControl? control,
        CancellationToken cancellationToken)
    {
        IAudioEngineSession session;
        try
        {
            var pictureTime = control?.WaitForPictureTimeUs(PictureClockWait, cancellationToken);
            session = engine.Start(options with { StartOffsetUs = pictureTime ?? options.StartOffsetUs });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AudioPumpEnd(default, null);
        }
        catch (AudioEngineException exception)
        {
            return new AudioPumpEnd(new AudioShareStats(0, 0, 0, AudioShareState.Unavailable), exception.Message, exception.Reason);
        }

        using (session)
        {
            try
            {
                Pump(session, transport, options, control, cancellationToken);
                return new AudioPumpEnd(session.ReadStats(), null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new AudioPumpEnd(session.ReadStats(), null);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                // The receiver session went away. Saying so is the picture's job; sound just stops.
                return new AudioPumpEnd(session.ReadStats(), null);
            }
            catch (MirrorEngineException exception)
            {
                return new AudioPumpEnd(session.ReadStats() with { State = AudioShareState.Unavailable }, exception.Message);
            }
        }
    }

    private void Pump(
        IAudioEngineSession session,
        IAudioTransport transport,
        AudioShareOptions options,
        MirrorControl? control,
        CancellationToken cancellationToken)
    {
        transport.SendAudioConfigAsync(session.Config, cancellationToken).GetAwaiter().GetResult();

        var buffer = new byte[PacketBufferBytes];
        var paused = false;
        var delay = options.DelayMilliseconds;
        var reportedAt = time.GetTimestamp();
        Report(session);
        while (!cancellationToken.IsCancellationRequested && transport.IsConnected)
        {
            // A paused share carries no sound: its picture has stopped, so its sound stops with it.
            var wanted = control?.Pause is { } pause && pause is not MirrorPause.Running;
            if (wanted != paused)
            {
                session.SetPaused(wanted);
                paused = wanted;
            }

            var asked = Volatile.Read(ref delayMilliseconds);
            if (asked >= 0 && asked != delay)
            {
                session.SetDelay(asked);
                delay = asked;
            }

            var length = session.Next(buffer, PacketWait, out var presentationTimeUs);

            // Also checked here, not only in the engine, because sound reaching the TV during a
            // pause is the one failure a person would notice and not forgive.
            if (length > 0 && !paused)
            {
                transport.SendAudioAsync(presentationTimeUs, BinaryData.From(buffer.AsSpan(0, length)), cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }

            if (time.GetElapsedTime(reportedAt) >= ReportEvery)
            {
                reportedAt = time.GetTimestamp();
                Report(session);
            }
        }
    }

    private void Report(IAudioEngineSession session)
    {
        var stats = session.ReadStats();
        Reported?.Invoke(new AudioPumpReport(stats, stats.State is AudioShareState.Unavailable ? session.ReadProblem() : null));
    }
}
