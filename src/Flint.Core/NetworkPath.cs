namespace Flint.Core;

/// <summary>
/// A measured view of the network path to a receiver.
/// </summary>
/// <remarks>
/// Every value here is measured. Nothing on this type may be estimated from link speed, radio band,
/// or any other proxy. Throughput is separable because latency can be measured against any open
/// port, while throughput needs a receiver willing to sink traffic — so the two become available at
/// different connection capabilities and must be reported independently.
/// </remarks>
/// <param name="RoundTripMs">Median round-trip time.</param>
/// <param name="JitterMs">Variation in round-trip time, which sizes the receiver's jitter buffer.</param>
/// <param name="ThroughputMbps">
/// Measured throughput, meaningful only when <paramref name="ThroughputMeasured"/> is
/// <see langword="true"/>.
/// </param>
/// <param name="PacketLossPercent">
/// The percentage of measurement attempts that received no response. It is not represented as
/// one-way media-packet loss until the receiver can measure that in a real session.
/// </param>
/// <param name="ThroughputMeasured">
/// Whether a throughput measurement actually ran. When <see langword="false"/>, Flint knows the
/// latency of this path but not its capacity, and must not claim otherwise.
/// </param>
public sealed record NetworkPath(
    double RoundTripMs,
    double JitterMs,
    double ThroughputMbps,
    double PacketLossPercent,
    bool ThroughputMeasured = true)
{
    /// <summary>
    /// The lowest throughput that supports a watchable 1080p60 mirror with FEC overhead.
    /// </summary>
    /// <remarks>
    /// 15 Mbit/s of video plus roughly 20% forward error correction, rounded up to leave the link
    /// some headroom. Below this, Flint reports the path as insufficient rather than starting a
    /// session that will look poor and be blamed on the encoder.
    /// </remarks>
    public const double MinimumMirrorThroughputMbps = 20.0;

    /// <summary>
    /// The round-trip time above which mirroring stops feeling attached to the mouse.
    /// </summary>
    public const double UsableRoundTripCeilingMs = 30.0;

    /// <summary>Whether the latency of this path is within reach of mirroring.</summary>
    public bool LatencySupportsMirroring => RoundTripMs <= UsableRoundTripCeilingMs;

    /// <summary>
    /// Whether throughput is known to be insufficient.
    /// </summary>
    /// <remarks>
    /// False when throughput was never measured. An unknown capacity is not a failing one.
    /// </remarks>
    public bool ThroughputTooLowForMirroring =>
        ThroughputMeasured && ThroughputMbps < MinimumMirrorThroughputMbps;

    /// <summary>Whether this path is proven able to carry a mirror session.</summary>
    public bool SupportsMirroring =>
        LatencySupportsMirroring && ThroughputMeasured && ThroughputMbps >= MinimumMirrorThroughputMbps;

    /// <summary>Throughput for display, or a dash when it was never measured.</summary>
    public string ThroughputLabel =>
        ThroughputMeasured ? $"{ThroughputMbps:F1} Mbit/s" : "Not measured";
}
