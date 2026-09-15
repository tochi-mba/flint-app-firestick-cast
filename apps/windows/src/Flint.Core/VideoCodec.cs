namespace Flint.Core;

/// <summary>
/// A video codec, using the same numeric identifiers as the REX wire protocol's <c>CodecId</c>
/// so host and receiver never disagree about what a number means.
/// </summary>
public enum VideoCodec
{
    /// <summary>H.264 / AVC. The widest Fire TV decoder support; the default.</summary>
    H264 = 1,

    /// <summary>H.265 / HEVC. Better compression, less uniform decoder support.</summary>
    H265 = 2,

    /// <summary>AV1. Best compression; decoder support on Fire TV hardware is rare.</summary>
    Av1 = 5,
}
