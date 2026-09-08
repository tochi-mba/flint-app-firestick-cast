namespace Flint.Core;

/// <summary>
/// The outcome of one <see cref="IMirrorEngineSession.Next"/> call.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="ByteCount">
/// Bytes written into the caller's buffer. Meaningful only when <paramref name="Kind"/> is
/// <see cref="MirrorTickKind.Encoded"/>.
/// </param>
/// <param name="KeyFrame">Whether a decoder can start from this access unit alone.</param>
/// <param name="PresentationTimeUs">Presentation time relative to the start of the session.</param>
public readonly record struct MirrorTick(
    MirrorTickKind Kind,
    int ByteCount,
    bool KeyFrame,
    long PresentationTimeUs)
{
    /// <summary>A tick that produced nothing.</summary>
    public static MirrorTick Nothing => new(MirrorTickKind.Nothing, 0, false, 0);
}
