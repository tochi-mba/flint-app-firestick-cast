namespace Flint.Core;

/// <summary>
/// A live capture-and-encode session owned by the engine.
/// </summary>
/// <remarks>
/// The managed side never runs per frame in the sense of doing work between two frames — it only
/// drains what the engine has already produced and puts it on the wire. Everything that must
/// happen between two frames lives behind this boundary.
/// </remarks>
public interface IMirrorEngineSession : IDisposable
{
    /// <summary>
    /// The codec this session's access units are actually in.
    /// </summary>
    /// <remarks>
    /// Reported by the engine rather than taken from what the handshake negotiated. A receiver
    /// told one codec and fed another builds the wrong decoder and shows black, and it does so
    /// nowhere near where the wrong codec was chosen.
    /// </remarks>
    VideoCodec Codec { get; }

    /// <summary>Encoded frame width, which may be smaller than the desktop's.</summary>
    int Width { get; }

    /// <summary>Encoded frame height.</summary>
    int Height { get; }

    /// <summary>
    /// The codec setup data the receiver needs before the first access unit.
    /// </summary>
    /// <remarks>
    /// For H.264 this is the SPS and PPS, as separate blocks, because that is the shape
    /// <c>MediaCodec.configure</c> wants them in on the receiver.
    /// </remarks>
    IReadOnlyList<byte[]> CodecSpecificData { get; }

    /// <summary>Runs one tick, writing any access unit into <paramref name="buffer"/>.</summary>
    /// <param name="buffer">Where an access unit is written. Never partially filled.</param>
    /// <returns>What the tick produced.</returns>
    /// <exception cref="MirrorEngineException">The engine could not complete the tick.</exception>
    MirrorTick Next(Span<byte> buffer);

    /// <summary>Asks for the next access unit to be a key frame.</summary>
    void RequestKeyFrame();

    /// <summary>Reads the session counters.</summary>
    MirrorSessionStats ReadStats();
}
