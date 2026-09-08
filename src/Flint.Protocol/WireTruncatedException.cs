namespace Flint.Protocol;

/// <summary>
/// Raised when a buffer ends before a frame does.
/// </summary>
/// <remarks>
/// Distinct from a general <see cref="WireFormatException"/> because a stream reader acts on the
/// difference: truncation means "read more bytes and try again", while any other format error means
/// the peer is not speaking this protocol and the session should close.
/// </remarks>
/// <param name="field">What was being read when the buffer ran out.</param>
public sealed class WireTruncatedException(string field)
    : WireFormatException($"Truncated {field}.")
{
    /// <summary>What was being read when the buffer ran out.</summary>
    public string Field { get; } = field;
}
