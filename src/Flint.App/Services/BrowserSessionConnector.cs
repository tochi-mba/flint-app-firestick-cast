using System.Net;
using Flint.Core;
using Flint.Protocol;
using Flint.Session.Browser;

namespace Flint.App.Services;

/// <summary>Host-side secure browser remote used by the Web destination UI.</summary>
public interface ISecureBrowserRemote : IAsyncDisposable
{
    /// <summary>Whether the TLS session can still send commands.</summary>
    bool IsConnected { get; }

    /// <summary>Accepted peer identity after first-use or reconnect trust.</summary>
    BrowserPeerIdentity PeerIdentity { get; }

    /// <summary>The authenticated receiver capabilities for this secure session.</summary>
    BrowserCapabilityMessage Capability { get; }

    /// <summary>Completes when the secure receive loop ends or fails.</summary>
    Task Completion { get; }

    /// <summary>Bounded receiver state updates.</summary>
    event Action<BrowserStateMessage>? StateReceived;

    /// <summary>Latest-only, opt-in preview frames from the television.</summary>
    event Action<BrowserPreviewMessage>? PreviewReceived;

    /// <summary>Native page dialogs that need an explicit answer.</summary>
    event Action<BrowserDialogMessage>? DialogReceived;

    /// <summary>Sends an ordered browser command over TLS.</summary>
    Task SendCommandAsync(BrowserCommandMessage command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one remote input over the same ordered TLS session.
    /// </summary>
    /// <remarks>
    /// Separate from commands because the receiver treats them differently: a command changes what
    /// page is loaded and an input acts on the page already there, and the receiver rejects an input
    /// whose epoch or sequence does not match the page it belongs to. Sending them down the same
    /// ordered stream is what keeps those two facts consistent.
    /// </remarks>
    Task SendInputAsync(BrowserInputMessage input, CancellationToken cancellationToken = default);

    /// <summary>Answers the one active native page dialog.</summary>
    Task SendDialogReplyAsync(
        BrowserDialogReplyMessage reply,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the additive cockpit channel over this session, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Separate from the rest of the remote because tabs, view settings and the library are a later
    /// family that an older receiver has never heard of. Identifiers are supplied by the caller so
    /// cockpit traffic shares the session's one ordered command sequence.
    /// </remarks>
    IBrowserCockpitRemote? CreateCockpit(Func<long> epoch, Func<long> nextCommandId);
}

/// <summary>Adapts <see cref="BrowserSession"/> to the UI remote surface.</summary>
public sealed class SecureBrowserRemote(BrowserSession session) : ISecureBrowserRemote
{
    private readonly BrowserSession session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public bool IsConnected => session.IsConnected;

    /// <inheritdoc />
    public BrowserPeerIdentity PeerIdentity => session.PeerIdentity;

    /// <inheritdoc />
    public BrowserCapabilityMessage Capability => session.Capability;

    /// <inheritdoc />
    public Task Completion => session.Completion;

    /// <inheritdoc />
    public event Action<BrowserStateMessage>? StateReceived
    {
        add => session.StateReceived += value;
        remove => session.StateReceived -= value;
    }

    /// <inheritdoc />
    public event Action<BrowserPreviewMessage>? PreviewReceived
    {
        add => session.PreviewReceived += value;
        remove => session.PreviewReceived -= value;
    }

    /// <inheritdoc />
    public event Action<BrowserDialogMessage>? DialogReceived
    {
        add => session.DialogReceived += value;
        remove => session.DialogReceived -= value;
    }

    /// <inheritdoc />
    public Task SendCommandAsync(BrowserCommandMessage command, CancellationToken cancellationToken = default) =>
        session.SendCommandAsync(command, cancellationToken);

    /// <inheritdoc />
    public Task SendInputAsync(BrowserInputMessage input, CancellationToken cancellationToken = default) =>
        session.SendInputAsync(input, cancellationToken);

    /// <inheritdoc />
    public Task SendDialogReplyAsync(
        BrowserDialogReplyMessage reply,
        CancellationToken cancellationToken = default) =>
        session.SendDialogReplyAsync(reply, cancellationToken);

    /// <inheritdoc />
    public IBrowserCockpitRemote? CreateCockpit(Func<long> epoch, Func<long> nextCommandId) =>
        new SecureBrowserCockpitRemote(session, epoch, nextCommandId);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => session.DisposeAsync();
}

/// <summary>Creates the dedicated TLS browser session used by the Web destination.</summary>
public interface IBrowserSessionConnector
{
    /// <summary>Connects, verifies trust, and returns an authenticated browser remote.</summary>
    Task<ISecureBrowserRemote> ConnectAsync(
        BrowserEndpoint endpoint,
        string pairingCode,
        IBrowserTrustStore trustStore,
        IBrowserTrustPrompter trustPrompter,
        CancellationToken cancellationToken = default);
}

/// <summary>Production connector that opens a real <see cref="BrowserSession"/>.</summary>
public sealed class BrowserSessionConnector : IBrowserSessionConnector
{
    /// <inheritdoc />
    public async Task<ISecureBrowserRemote> ConnectAsync(
        BrowserEndpoint endpoint,
        string pairingCode,
        IBrowserTrustStore trustStore,
        IBrowserTrustPrompter trustPrompter,
        CancellationToken cancellationToken = default)
    {
        var session = await BrowserSession.ConnectAsync(
                endpoint,
                pairingCode,
                trustStore,
                trustPrompter,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new SecureBrowserRemote(session);
    }
}

/// <summary>Resolves the selected Cast probe into an untrusted browser TLS route.</summary>
public static class BrowserEndpointResolver
{
    /// <summary>Stable trust-store key shared with the receiver's default identity alias.</summary>
    public const string DefaultReceiverIdentity = "flint-browser-receiver";

    /// <summary>
    /// Builds a browser endpoint from the selected device's advertised port, or returns null when
    /// routing metadata is missing.
    /// </summary>
    public static BrowserEndpoint? TryResolve(FireTvDevice? device, int? manualPort = null)
    {
        if (device is null || device.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return null;
        }

        var port = manualPort
            ?? (device.BrowserEvidence is { SecureEndpointAvailable: true, SecureEndpointPort: { } advertised }
                ? advertised
                : null);
        if (port is null or < 1 or > 65_535)
        {
            return null;
        }

        return new BrowserEndpoint(device.Address, port.Value, DefaultReceiverIdentity);
    }
}
