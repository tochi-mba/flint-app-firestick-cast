namespace Flint.Core;

/// <summary>
/// A hardware video encoder that this host actually has, as proven by a capability probe.
/// </summary>
/// <remarks>
/// A present GPU does not imply a present encoder. Encode support varies by silicon generation, by
/// codec, and by driver version, and a virtualised session may expose none at all. Instances of
/// this type are only ever created from a successful probe, never from a vendor string.
/// </remarks>
/// <param name="Vendor">The encoder family.</param>
/// <param name="AdapterLuid">The adapter this encoder belongs to, matching <see cref="DisplayAdapter.Luid"/>.</param>
/// <param name="Codecs">Codecs the probe confirmed this encoder can produce.</param>
public sealed record HostVideoEncoder(
    EncoderVendor Vendor,
    long AdapterLuid,
    IReadOnlySet<VideoCodec> Codecs)
{
    /// <summary>Whether this encoder is fit for a live session.</summary>
    /// <remarks>Software encoders are excluded: they cannot meet the latency budget.</remarks>
    public bool IsSessionCapable => Vendor is not EncoderVendor.Software && Codecs.Count > 0;

    /// <summary>Whether the probe confirmed support for a specific codec.</summary>
    public bool Supports(VideoCodec codec) => Codecs.Contains(codec);
}
