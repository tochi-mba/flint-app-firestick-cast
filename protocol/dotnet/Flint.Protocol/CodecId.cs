namespace Flint.Protocol;

/// <summary>
/// A numeric media codec identifier.
/// </summary>
/// <remarks>
/// A wrapper over a number rather than an enum, deliberately: a future codec must stay
/// representable so an older build can relay a frame it does not itself understand.
/// </remarks>
/// <param name="Value">The wire value. Never zero.</param>
public readonly record struct CodecId(int Value)
{
    /// <summary>H.264 / AVC.</summary>
    public static readonly CodecId H264 = new(1);

    /// <summary>H.265 / HEVC.</summary>
    public static readonly CodecId H265 = new(2);

    /// <summary>AAC low complexity.</summary>
    public static readonly CodecId AacLc = new(3);

    /// <summary>Opus.</summary>
    public static readonly CodecId Opus = new(4);

    /// <summary>AV1. A Flint extension; REX Cast does not name it yet.</summary>
    public static readonly CodecId Av1 = new(5);

    /// <summary>
    /// Validates the wire value.
    /// </summary>
    /// <remarks>
    /// Zero is reserved so a zeroed buffer can never decode as a valid codec.
    /// </remarks>
    public bool IsValid => Value is > 0 and <= 0xFFFF;
}
