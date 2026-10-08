using Flint.Protocol;

namespace Flint.Session;

/// <summary>Where shared sound goes: the TV's sound decoder.</summary>
/// <remarks>
/// Separate from <see cref="IMirrorTransport"/>, as <see cref="IMirrorFeedbackTransport"/> is, so a
/// transport that carries only the picture need not implement it.
/// </remarks>
public interface IAudioTransport
{
    /// <summary>Whether the TV is still connected.</summary>
    bool IsConnected { get; }

    /// <summary>Configures the TV's sound decoder for AAC-LC at 48 kHz stereo.</summary>
    Task SendAudioConfigAsync(IReadOnlyList<byte> config, CancellationToken cancellationToken = default);

    /// <summary>Sends one encoded sound frame.</summary>
    Task SendAudioAsync(long presentationTimeUs, BinaryData data, CancellationToken cancellationToken = default);
}
