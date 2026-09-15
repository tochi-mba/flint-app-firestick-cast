using System.Net;
using System.Net.Sockets;

namespace Flint.Session.Browser;

/// <summary>Untrusted routing metadata for a selected receiver's dedicated browser TLS listener.</summary>
/// <remarks>
/// <see cref="Address"/> and <see cref="Port"/> decide where to attempt a connection. They never
/// establish identity; a full SPKI pin does that after the TLS certificate arrives.
/// </remarks>
public sealed record BrowserEndpoint
{
    /// <summary>Creates a bounded IPv4 endpoint and its stable selected-receiver identity.</summary>
    public BrowserEndpoint(IPAddress address, int port, string receiverIdentity)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException("Browser endpoints must be explicitly selected IPv4 addresses.", nameof(address));
        }
        if (port is < 1 or > 65_535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }
        if (string.IsNullOrWhiteSpace(receiverIdentity) || receiverIdentity.Length > 512)
        {
            throw new ArgumentException("Receiver identity must be a non-empty bounded value.", nameof(receiverIdentity));
        }

        Address = address;
        Port = port;
        ReceiverIdentity = receiverIdentity;
    }

    /// <summary>Explicit route selected from non-secret discovery metadata.</summary>
    public IPAddress Address { get; init; }

    /// <summary>Dedicated TLS listener port from current discovery metadata.</summary>
    public int Port { get; init; }

    /// <summary>Stable receiver identity key selected by the application, not a trust credential.</summary>
    public string ReceiverIdentity { get; init; }
}

/// <summary>The endpoint and certificate identity shown for a first-use physical-screen comparison.</summary>
public sealed record BrowserPeerIdentity(BrowserEndpoint Endpoint, BrowserFingerprint Fingerprint);

/// <summary>
/// Asks the UI to obtain an explicit human first-use decision after it shows the same short code as
/// the TV. Reconnects with an existing exact pin never invoke this interaction.
/// </summary>
public interface IBrowserTrustPrompter
{
    /// <summary>Returns true only when the user has compared and accepted the displayed code.</summary>
    ValueTask<bool> ConfirmFirstUseAsync(BrowserPeerIdentity identity, CancellationToken cancellationToken = default);
}

/// <summary>Signals a TLS-browser framing, version, route, or direction violation.</summary>
public sealed class BrowserProtocolException : IOException
{
    /// <inheritdoc />
    public BrowserProtocolException(string message)
        : base(message)
    {
    }

    /// <inheritdoc />
    public BrowserProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Signals that the authenticated receiver did not make the browser capability available.</summary>
public sealed class BrowserCapabilityException : IOException
{
    /// <inheritdoc />
    public BrowserCapabilityException(string message)
        : base(message)
    {
    }
}
