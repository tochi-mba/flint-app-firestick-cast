namespace Flint.Protocol;

/// <summary>
/// Raised when a payload version falls outside what this build implements.
/// </summary>
/// <param name="version">The version that was refused.</param>
public sealed class UnsupportedProtocolVersionException(int version)
    : WireFormatException($"Unsupported wire payload version: {version}")
{
    /// <summary>The version that was refused.</summary>
    public int Version { get; } = version;
}
