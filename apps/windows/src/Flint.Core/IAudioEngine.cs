namespace Flint.Core;

/// <summary>One of this PC's sound outputs.</summary>
/// <param name="Id">Windows' lasting identity for it.</param>
/// <param name="Name">The name Windows shows for it.</param>
/// <param name="IsDefault">Whether Windows plays through it by default.</param>
public sealed record AudioDevice(string Id, string Name, bool IsDefault);

/// <summary>What a sound share is doing.</summary>
/// <remarks>Values match <c>audio_state</c> in the engine's <c>ffi_audio.rs</c>.</remarks>
public enum AudioShareState
{
    /// <summary>Not sharing sound.</summary>
    Off = 0,

    /// <summary>Running, and nothing is playing on this PC yet.</summary>
    Ready = 1,

    /// <summary>Running, and sound is reaching the TV.</summary>
    Sounding = 2,

    /// <summary>Stopped by a problem.</summary>
    Unavailable = 3,
}

/// <summary>Why a sound share could not start.</summary>
/// <remarks>Values match <c>audio_failure</c> in the engine's <c>ffi_audio.rs</c>, with two of Flint's own.</remarks>
public enum AudioStartFailure
{
    /// <summary>It started.</summary>
    None = 0,

    /// <summary>This edition of Windows has no AAC encoder.</summary>
    NoEncoder = 1,

    /// <summary>The data rate is not one Windows offers.</summary>
    UnsupportedBitrate = 2,

    /// <summary>This PC has no sound output.</summary>
    NoOutput = 3,

    /// <summary>The chosen output is not connected.</summary>
    DeviceMissing = 4,

    /// <summary>Windows refused for another reason.</summary>
    Platform = 5,

    /// <summary>This build's engine has no sound.</summary>
    EngineMissing = 100,
}

/// <summary>What a sound share is asked to do.</summary>
/// <param name="DeviceId">The output to capture, or null for the default output.</param>
/// <param name="BitrateKbps">The data rate: 96, 128, 160 or 192.</param>
/// <param name="StartOffsetUs">Added to every packet's time, so sound shares the picture's clock.</param>
/// <param name="DelayMilliseconds">How long every packet is held back.</param>
public sealed record AudioShareOptions(
    string? DeviceId = null,
    int BitrateKbps = 128,
    long StartOffsetUs = 0,
    int DelayMilliseconds = 0);

/// <summary>A sound share's counters and state.</summary>
/// <param name="Packets">Packets encoded.</param>
/// <param name="Dropped">Packets dropped because the network fell behind.</param>
/// <param name="Level">The level of what was captured, from 0 to 1.</param>
/// <param name="State">What the share is doing.</param>
/// <param name="Meter">
/// The level Windows' own meter shows for the output, from 0 to 1. It is measured before the
/// output's mute, so sound here while <paramref name="Level"/> is silent means capture cannot hear
/// the output.
/// </param>
public readonly record struct AudioShareStats(long Packets, long Dropped, float Level, AudioShareState State, float Meter = 0);

/// <summary>A sound share that could not start, and why.</summary>
public sealed class AudioEngineException : Exception
{
    /// <summary>Creates the exception.</summary>
    public AudioEngineException(string message, AudioStartFailure reason, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>Why it could not start.</summary>
    public AudioStartFailure Reason { get; }
}

/// <summary>Captures what this PC plays and encodes it for the TV.</summary>
public interface IAudioEngine
{
    /// <summary>Lists the sound outputs, or none when they cannot be listed.</summary>
    IReadOnlyList<AudioDevice> ListDevices();

    /// <summary>Starts a sound share.</summary>
    /// <exception cref="AudioEngineException">It could not start.</exception>
    IAudioEngineSession Start(AudioShareOptions options);

    /// <summary>Whether an output is muted: the named one, or the default when null; null when unknown.</summary>
    bool? IsMuted(string? deviceId);

    /// <summary>Mutes or unmutes an output, leaving its level alone.</summary>
    /// <returns>Whether Windows did it.</returns>
    bool SetMuted(string? deviceId, bool muted);
}

/// <summary>A running sound share.</summary>
public interface IAudioEngineSession : IDisposable
{
    /// <summary>The two-byte AudioSpecificConfig the TV's decoder needs first.</summary>
    IReadOnlyList<byte> Config { get; }

    /// <summary>Waits up to <paramref name="timeout"/> for the next packet, written into <paramref name="buffer"/>.</summary>
    /// <returns>The packet's length, or zero when none was ready.</returns>
    int Next(Span<byte> buffer, TimeSpan timeout, out long presentationTimeUs);

    /// <summary>Reads the counters and the state.</summary>
    AudioShareStats ReadStats();

    /// <summary>Why the share is unavailable, or null when it is not.</summary>
    string? ReadProblem();

    /// <summary>Pauses or resumes; while paused nothing captured is sent.</summary>
    void SetPaused(bool paused);

    /// <summary>Holds packets from now on back by <paramref name="milliseconds"/>.</summary>
    void SetDelay(int milliseconds);
}
