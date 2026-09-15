namespace Flint.Discovery;

/// <summary>
/// Raised when a DNS packet cannot be read.
/// </summary>
/// <remarks>
/// Routine during multicast discovery. Flint listens on a shared multicast group and will see
/// packets from unrelated responders, so callers skip the packet rather than failing the scan.
/// </remarks>
public sealed class DnsFormatException(string message) : Exception(message);
