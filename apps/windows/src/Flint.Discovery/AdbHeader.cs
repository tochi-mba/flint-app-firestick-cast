namespace Flint.Discovery;

/// <summary>
/// A parsed ADB message header.
/// </summary>
/// <param name="Command">The command tag.</param>
/// <param name="Arg0">First argument.</param>
/// <param name="Arg1">Second argument.</param>
/// <param name="PayloadLength">Bytes of payload still to read from the stream.</param>
/// <param name="Checksum">
/// The declared payload checksum. Recorded but not enforced: newer devices send zero.
/// </param>
public readonly record struct AdbHeader(
    AdbCommand Command,
    uint Arg0,
    uint Arg1,
    int PayloadLength,
    uint Checksum);
