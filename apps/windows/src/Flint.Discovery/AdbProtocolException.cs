namespace Flint.Discovery;

/// <summary>
/// Raised when a peer that answered on an ADB port is not speaking the ADB protocol.
/// </summary>
/// <remarks>
/// Expected during a port scan rather than exceptional: the scanned range overlaps services that
/// have nothing to do with Fire TV. Callers treat this as "not a device", never as a failure.
/// </remarks>
public sealed class AdbProtocolException(string message) : Exception(message);
