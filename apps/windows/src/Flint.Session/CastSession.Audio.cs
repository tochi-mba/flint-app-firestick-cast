using Flint.Protocol;

namespace Flint.Session;

/// <summary>Sending this PC's sound to the TV.</summary>
public sealed partial class CastSession : IAudioTransport
{
    /// <summary>The rate shared sound is captured, encoded and played at.</summary>
    public const int AudioSampleRateHz = 48_000;

    /// <summary>Shared sound is always stereo.</summary>
    public const int AudioChannels = 2;

    /// <inheritdoc />
    public Task SendAudioConfigAsync(IReadOnlyList<byte> config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        return SendAsync(
            new AudioConfigMessage(CodecId.AacLc, AudioSampleRateHz, AudioChannels, BinaryData.From([.. config])),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SendAudioAsync(long presentationTimeUs, BinaryData data, CancellationToken cancellationToken = default) =>
        SendAsync(new AudioPacket(presentationTimeUs, data), cancellationToken);
}
