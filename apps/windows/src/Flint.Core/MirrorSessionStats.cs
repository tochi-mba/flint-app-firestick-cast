namespace Flint.Core;

/// <summary>
/// Counters a mirror session accumulates, for the diagnostics view and for adaptive bitrate.
/// </summary>
/// <param name="FramesEncoded">Frames the encoder produced an access unit for.</param>
/// <param name="FramesUnchanged">Ticks where the desktop had not changed.</param>
/// <param name="Recoveries">Times capture was lost and re-created.</param>
/// <param name="BytesEncoded">Total encoded bytes handed to the transport.</param>
public readonly record struct MirrorSessionStats(
    long FramesEncoded,
    long FramesUnchanged,
    long Recoveries,
    long BytesEncoded);
