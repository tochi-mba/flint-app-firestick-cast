namespace Flint.Discovery;

/// <summary>
/// ADB wire commands, as the four-byte little-endian tags they appear as on the wire.
/// </summary>
/// <remarks>
/// Flint only needs the handshake. Connecting and reading the peer's reply is enough to tell an
/// Android device from silence, and an authorised device from one showing an RSA prompt.
/// </remarks>
public enum AdbCommand : uint
{
    /// <summary>Connect. Sent by Flint, and echoed by an authorised device with its banner.</summary>
    Connect = 0x4E584E43,

    /// <summary>Authenticate. The device is asking for a key, which means it is not yet authorised.</summary>
    Auth = 0x48545541,

    /// <summary>Open a stream.</summary>
    Open = 0x4E45504F,

    /// <summary>Stream ready.</summary>
    Okay = 0x59414B4F,

    /// <summary>Close a stream.</summary>
    Close = 0x45534C43,

    /// <summary>Write to a stream.</summary>
    Write = 0x45545257,

    /// <summary>Begin TLS. Newer devices may offer this instead of the key exchange.</summary>
    StartTls = 0x534C5453,
}
