namespace Flint.Core;

/// <summary>An audio engine with no sound at all: for tests and design-time shells.</summary>
/// <remarks>
/// The default wherever an engine is not supplied, so nothing built for a test ever captures this
/// PC's sound or touches its mute.
/// </remarks>
public sealed class UnavailableAudioEngine : IAudioEngine
{
    /// <inheritdoc />
    public IReadOnlyList<AudioDevice> ListDevices() => [];

    /// <inheritdoc />
    public IAudioEngineSession Start(AudioShareOptions options) =>
        throw new AudioEngineException("This build of Flint cannot share sound.", AudioStartFailure.EngineMissing);

    /// <inheritdoc />
    public bool? IsMuted(string? deviceId) => null;

    /// <inheritdoc />
    public bool SetMuted(string? deviceId, bool muted) => false;
}
