using Flint.Core;

namespace Flint.Session;

/// <summary>What a share's current session sends.</summary>
/// <param name="Width">The encoded picture's width.</param>
/// <param name="Height">The encoded picture's height.</param>
/// <param name="Encoder">Where it is encoded.</param>
public sealed record MirrorPicture(int Width, int Height, MirrorEncoderKind Encoder);

/// <summary>How a request to change a running share ended.</summary>
/// <param name="Options">What the share is now sending.</param>
/// <param name="Failure">Why the change could not be made, or null when it was.</param>
public sealed record MirrorSwitch(MirrorSessionOptions Options, string? Failure)
{
    /// <summary>Whether the share now sends what was asked for.</summary>
    public bool Succeeded => Failure is null;
}

/// <summary>Changes a running screen share from outside the worker that runs it.</summary>
/// <remarks>
/// The native session belongs to one worker thread, so nothing here touches it. A change is left
/// in a slot the worker reads between frames; a second change before the worker gets to the first
/// replaces it, so only the latest is applied.
/// </remarks>
public sealed class MirrorControl
{
    /// <summary>
    /// How many access units may still reach the TV after a black-screen pause begins: the black
    /// frame, and what the encoder was still finishing from before the pause.
    /// </summary>
    /// <remarks>
    /// A ceiling the runner holds whatever the engine does, because a picture leaking out of a
    /// paused share is a privacy failure rather than a glitch.
    /// </remarks>
    public const int BlackFrameAllowance = 4;

    private readonly TimeProvider time;
    private readonly TaskCompletionSource clockStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MirrorSessionOptions? pending;
    private int pause;
    private int showPointer = 1;
    private long clockOrigin;

    /// <summary>Creates a control on the system clock.</summary>
    public MirrorControl()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a control that reads the picture's clock through <paramref name="time"/>.</summary>
    public MirrorControl(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        this.time = time;
    }

    /// <summary>
    /// Raised on the share's worker thread once a change has been made, or refused and the share
    /// carried on as it was.
    /// </summary>
    public event Action<MirrorSwitch>? Switched;

    /// <summary>Whether the share should be sending, and what the TV shows while it is not.</summary>
    public MirrorPause Pause => (MirrorPause)Volatile.Read(ref pause);

    /// <summary>Pauses or resumes the share, from its next frame.</summary>
    /// <remarks>A change asked for while paused waits, and is made on resuming.</remarks>
    public void SetPause(MirrorPause value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Not a pause the engine knows.");
        }

        Volatile.Write(ref pause, (int)value);
    }

    /// <summary>Whether the mouse pointer is drawn into the picture. On unless switched off.</summary>
    public bool ShowPointer => Volatile.Read(ref showPointer) != 0;

    /// <summary>Draws the mouse pointer into the picture from the next frame, or stops.</summary>
    public void SetShowPointer(bool show) => Volatile.Write(ref showPointer, show ? 1 : 0);

    /// <summary>Asks the share to send <paramref name="options"/> from now on.</summary>
    public void Change(MirrorSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Volatile.Write(ref pending, options);
    }

    /// <summary>The picture's clock now, in microseconds, or null until the share has started it.</summary>
    /// <remarks>
    /// Sound shares this clock, so the TV can line the two up. It is read here rather than from the
    /// engine because the engine's session belongs to the share's worker thread.
    /// </remarks>
    public long? ReadPictureTimeUs() =>
        clockStarted.Task.IsCompleted
            ? (long)time.GetElapsedTime(Volatile.Read(ref clockOrigin)).TotalMicroseconds
            : null;

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the share to start the picture's clock, for sound
    /// switched on at the same moment as the picture.
    /// </summary>
    /// <returns>The picture's clock now, or null when it did not start in time.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public long? WaitForPictureTimeUs(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _ = clockStarted.Task.Wait(timeout, cancellationToken);
        return ReadPictureTimeUs();
    }

    /// <summary>Starts the picture's clock: it read <paramref name="elapsedUs"/> just now.</summary>
    internal void StartClock(long elapsedUs)
    {
        var ticks = (long)(elapsedUs / 1_000_000.0 * time.TimestampFrequency);
        Volatile.Write(ref clockOrigin, time.GetTimestamp() - ticks);
        clockStarted.TrySetResult();
    }

    /// <summary>Takes the waiting change, if there is one.</summary>
    internal MirrorSessionOptions? TakeChange() => Interlocked.Exchange(ref pending, null);

    /// <summary>Says how a change ended.</summary>
    internal void Report(MirrorSwitch result) => Switched?.Invoke(result);
}
