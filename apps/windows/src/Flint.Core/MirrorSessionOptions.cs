namespace Flint.Core;

/// <summary>
/// What a mirror session should produce.
/// </summary>
/// <param name="OutputIndex">Which display to capture, counted in adapter enumeration order.</param>
/// <param name="FrameRate">Target frames per second.</param>
/// <param name="BitrateBitsPerSecond">Target bitrate.</param>
/// <param name="MaxWidth">
/// The longest horizontal edge to encode, or zero to encode at the desktop's own size. A 4K
/// desktop mirrored to a 1080p television gains nothing from encoding at source resolution and
/// costs both encode time and bandwidth, so this is capped rather than discovered per frame.
/// </param>
public sealed record MirrorSessionOptions(
    uint OutputIndex = 0,
    uint FrameRate = 30,
    uint BitrateBitsPerSecond = 12_000_000,
    uint MaxWidth = 1920);
