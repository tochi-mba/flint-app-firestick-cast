namespace Flint.Protocol;

/// <summary>
/// Raised when a frame cannot be read or written.
/// </summary>
/// <remarks>
/// Expected rather than exceptional on the receive path: a frame arrives from an unauthenticated
/// peer on the local network, and rejecting a malformed one is normal operation. Callers close the
/// session; they do not treat it as a crash.
/// </remarks>
public class WireFormatException(string message) : Exception(message);
