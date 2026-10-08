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

    private MirrorSessionOptions? pending;
    private int pause;

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

    /// <summary>Asks the share to send <paramref name="options"/> from now on.</summary>
    public void Change(MirrorSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Volatile.Write(ref pending, options);
    }

    /// <summary>Takes the waiting change, if there is one.</summary>
    internal MirrorSessionOptions? TakeChange() => Interlocked.Exchange(ref pending, null);

    /// <summary>Says how a change ended.</summary>
    internal void Report(MirrorSwitch result) => Switched?.Invoke(result);
}
