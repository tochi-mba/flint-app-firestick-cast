using Flint.Protocol;

namespace Flint.Session;

/// <summary>Receiver feedback available to a live mirror transport.</summary>
/// <remarks>
/// Kept separate from <see cref="IMirrorTransport"/> so simple transports and tests do not need
/// to manufacture feedback. The runner uses increasing dropped-frame counters as an explicit
/// request for a fresh IDR; healthy streams retain an unbounded GOP.
/// </remarks>
public interface IMirrorFeedbackTransport
{
    /// <summary>Raised when the receiver publishes its latest decoder counters.</summary>
    event Action<StatsMessage>? StatsReceived;
}
