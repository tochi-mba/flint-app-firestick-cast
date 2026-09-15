using Flint.Protocol;

namespace Flint.Session;

/// <summary>
/// The part of a receiver session a mirror stream needs.
/// </summary>
/// <remarks>
/// <see cref="CastSession"/> is the real implementation. This exists so the mirror loop can be
/// driven and asserted on without a socket, because the interesting failures in that loop — a
/// dropped key frame, a stall reported as a success, a surface never announced — are all logic and
/// none of them need a network to reproduce.
/// </remarks>
public interface IMirrorTransport
{
    /// <summary>Whether the session is still usable.</summary>
    bool IsConnected { get; }

    /// <summary>Selects the receiver surface that will show the stream.</summary>
    Task SendSurfaceAsync(SurfaceMessage surface, CancellationToken cancellationToken = default);

    /// <summary>Sends the decoder configuration before the first access unit.</summary>
    /// <param name="codec">
    /// The codec the access units are actually in. Passed explicitly rather than taken from the
    /// codec the handshake negotiated: the two can differ, and when they do the receiver builds a
    /// decoder for a stream it will never be sent.
    /// </param>
    /// <param name="width">Encoded frame width.</param>
    /// <param name="height">Encoded frame height.</param>
    /// <param name="codecSpecificData">Decoder setup blocks, in the order the receiver wants them.</param>
    /// <param name="cancellationToken">Abandons the send.</param>
    Task SendVideoConfigAsync(
        CodecId codec,
        int width,
        int height,
        IEnumerable<BinaryData> codecSpecificData,
        CancellationToken cancellationToken = default);

    /// <summary>Sends one encoded access unit.</summary>
    Task SendVideoAsync(
        long presentationTimeUs,
        bool keyFrame,
        BinaryData data,
        CancellationToken cancellationToken = default);
}
