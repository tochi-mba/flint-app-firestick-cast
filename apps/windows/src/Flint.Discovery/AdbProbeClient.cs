using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Flint.Core;

namespace Flint.Discovery;

/// <summary>Identifies an advertised Fire TV through the smallest safe ADB exchange.</summary>
/// <remarks>
/// A first connection may offer Flint's public key and cause the television's normal RSA prompt.
/// Flint waits for a bounded period so an explicit acceptance can finish that same probe; it never
/// bypasses the prompt or assumes consent. Once authorised, a probe reads only Android build
/// properties. The three things that do change the television — opening the receiver, asking what
/// is installed, and installing the bundled receiver — are separate calls, each behind its own
/// explicit action in the shell, and each names exactly what it does.
/// </remarks>
public sealed class AdbProbeClient
{
    /// <summary>Lowest port in the range Fire TV devices expose ADB on.</summary>
    public const int FirstPort = 5555;

    /// <summary>Highest port in the scanned range.</summary>
    public const int LastPort = 5585;

    /// <summary>How long a single connect attempt may take.</summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(600);

    /// <summary>How long the identity exchange and read-only property query may take.</summary>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(4);

    /// <summary>How long a first-time probe leaves the television's RSA consent prompt active.</summary>
    public static readonly TimeSpan AuthorizationPromptTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long an install may take from the first byte to the package manager's answer.
    /// </summary>
    /// <remarks>
    /// The package manager answers once it has read every byte and finished installing, which on a
    /// Fire TV can be tens of seconds after the last byte went out.
    /// </remarks>
    public static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(3);

    private readonly IAdbIdentityProvider _identities;

    /// <summary>Creates a probe using Flint's persistent, per-user ADB host identity.</summary>
    public AdbProbeClient()
        : this(new FileAdbIdentityProvider())
    {
    }

    internal AdbProbeClient(IAdbIdentityProvider identities) =>
        _identities = identities ?? throw new ArgumentNullException(nameof(identities));

    /// <summary>Probes one address across the Fire TV ADB port range.</summary>
    public async Task<AdbProbeResult> ProbeAsync(
        IPAddress address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        for (var port = FirstPort; port <= LastPort; port++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ProbePortAsync(address, port, cancellationToken).ConfigureAwait(false);
            if (result.State is not AdbConnectionState.Refused)
            {
                return result;
            }
        }

        return AdbProbeResult.NotFound;
    }

    /// <summary>Probes one explicit address and port.</summary>
    public async Task<AdbProbeResult> ProbePortAsync(
        IPAddress address,
        int port,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        ValidatePort(port);

        using var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(ConnectTimeout);
            await client.ConnectAsync(address, port, connectTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AdbProbeResult.NotFound;
        }
        catch (SocketException)
        {
            return AdbProbeResult.NotFound;
        }

        try
        {
            var stream = client.GetStream();
            var (result, _) = await AuthenticateAsync(stream, port, cancellationToken).ConfigureAwait(false);
            if (result.State is not AdbConnectionState.Connected)
            {
                return result;
            }

            using var propertyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            propertyTimeout.CancelAfter(HandshakeTimeout);
            try
            {
                var output = await RunServiceAsync(stream, "shell:getprop", payload: null, chunkLength: 0, progress: null, propertyTimeout.Token)
                    .ConfigureAwait(false);
                var properties = AdbBuildProperties.Parse(output);
                return result with
                {
                    AndroidApiLevel = properties.AndroidApiLevel,
                    AndroidRelease = properties.AndroidRelease,
                    Model = properties.Model,
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return result;
            }
            catch (Exception exception) when (exception is IOException or AdbProtocolException)
            {
                return result;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new AdbProbeResult(AdbConnectionState.TimedOut, port, Banner: null);
        }
        catch (Exception exception) when (exception is SocketException or IOException or AdbProtocolException)
        {
            return AdbProbeResult.NotFound;
        }
    }

    /// <summary>Brings an installed receiver activity to the foreground over an authenticated ADB link.</summary>
    public async Task LaunchActivityAsync(
        IPAddress address,
        int port,
        string packageName,
        string activityName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        if (!IsAndroidComponentName(packageName) || !IsAndroidComponentName(activityName))
        {
            throw new ArgumentException("The Android component name contains unsupported characters.");
        }

        using var client = await ConnectAuthorizedAsync(address, port, "open the receiver", cancellationToken)
            .ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HandshakeTimeout);
        var output = await RunServiceAsync(
            client.Client.GetStream(),
            $"shell:am start -n {packageName}/{activityName}",
            payload: null,
            chunkLength: 0,
            progress: null,
            timeout.Token).ConfigureAwait(false);
        if (output.Contains("Error", StringComparison.OrdinalIgnoreCase))
        {
            throw new AdbProtocolException($"Fire TV could not open the receiver: {output.Trim()}");
        }
    }

    /// <summary>
    /// Asks the television which of <paramref name="candidates"/> is installed, and at what version.
    /// </summary>
    /// <remarks>
    /// Read-only: a package listing and one <c>dumpsys</c>. The first candidate found wins, so the
    /// caller lists the package it would install first.
    /// </remarks>
    /// <returns>The installed package, or <see langword="null"/> when none of the candidates is there.</returns>
    public async Task<InstalledReceiver?> FindInstalledPackageAsync(
        IPAddress address,
        int port,
        IEnumerable<string> candidates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var wanted = candidates.ToArray();
        if (wanted.Length == 0 || wanted.Any(name => !IsAndroidComponentName(name)))
        {
            throw new ArgumentException("Every candidate must be an Android package name.", nameof(candidates));
        }

        using var client = await ConnectAuthorizedAsync(address, port, "read what is installed", cancellationToken)
            .ConfigureAwait(false);
        var stream = client.Client.GetStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HandshakeTimeout);

        var listing = await RunServiceAsync(
            stream,
            $"shell:pm list packages {AdbPackageInventory.PackagePrefix}",
            payload: null,
            chunkLength: 0,
            progress: null,
            timeout.Token).ConfigureAwait(false);
        var installed = AdbPackageInventory.ParseListing(listing);
        var found = wanted.FirstOrDefault(installed.Contains);
        if (found is null)
        {
            return null;
        }

        var dump = await RunServiceAsync(
            stream,
            $"shell:dumpsys package {found}",
            payload: null,
            chunkLength: 0,
            progress: null,
            timeout.Token).ConfigureAwait(false);
        return AdbPackageInventory.ParseDump(found, dump);
    }

    /// <summary>
    /// Installs an APK, replacing an existing install of the same package in place.
    /// </summary>
    /// <remarks>
    /// Streamed straight into the package manager (<c>cmd package install -S</c>). Nothing is
    /// written to the television's storage first, so a failed install cannot strand a file the
    /// user then has to find and delete. Android itself refuses a downgrade or a package signed by
    /// somebody else; that refusal comes back verbatim in the outcome. The progress reported is the
    /// fraction of bytes the television has acknowledged.
    /// </remarks>
    public async Task<AdbInstallOutcome> InstallPackageAsync(
        IPAddress address,
        int port,
        ReadOnlyMemory<byte> apk,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (apk.IsEmpty)
        {
            throw new ArgumentException("The package is empty.", nameof(apk));
        }

        using var client = await ConnectAuthorizedAsync(address, port, "install the receiver", cancellationToken)
            .ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(InstallTimeout);
        var chunkLength = (int)Math.Min(AdbMessage.MaxPayloadLength, client.RemoteMaxData);
        var output = await RunServiceAsync(
            client.Client.GetStream(),
            $"exec:cmd package install -r -S {apk.Length}",
            apk,
            chunkLength,
            progress,
            timeout.Token).ConfigureAwait(false);
        return AdbInstallOutcome.FromOutput(output);
    }

    /// <summary>An open, authorised connection and what the peer said it can take per message.</summary>
    private sealed class AuthorizedClient(TcpClient client, uint remoteMaxData) : IDisposable
    {
        public TcpClient Client { get; } = client;

        public uint RemoteMaxData { get; } = remoteMaxData;

        public void Dispose() => Client.Dispose();
    }

    private async Task<AuthorizedClient> ConnectAuthorizedAsync(
        IPAddress address,
        int port,
        string purpose,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        ValidatePort(port);

        var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(ConnectTimeout);
            try
            {
                await client.ConnectAsync(address, port, connectTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException("The Fire TV did not accept the ADB connection in time.");
            }
            catch (SocketException exception)
            {
                throw new IOException("The Fire TV did not accept the ADB connection.", exception);
            }

            var (result, remoteMaxData) = await AuthenticateAsync(client.GetStream(), port, cancellationToken)
                .ConfigureAwait(false);
            if (result.State is not AdbConnectionState.Connected)
            {
                throw new IOException($"The Fire TV did not authorize Flint to {purpose}.");
            }

            return new AuthorizedClient(client, remoteMaxData);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs the ADB greeting and, if the television asks, the RSA exchange.
    /// </summary>
    /// <returns>
    /// The connection state with the banner, and the largest payload the peer accepts per message.
    /// </returns>
    private async Task<(AdbProbeResult Result, uint RemoteMaxData)> AuthenticateAsync(
        NetworkStream stream,
        int port,
        CancellationToken callerCancellation)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        handshakeTimeout.CancelAfter(HandshakeTimeout);
        var deadline = handshakeTimeout.Token;

        await WriteAsync(stream, AdbMessage.Connect(), deadline).ConfigureAwait(false);
        var reply = await ReadAsync(stream, deadline).ConfigureAwait(false);

        if (reply.Command is AdbCommand.StartTls)
        {
            return (new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null), 0);
        }

        if (reply.Command is AdbCommand.Auth)
        {
            if (reply.Arg0 != AdbAuthentication.TokenType
                || reply.Payload.Length != AdbAuthentication.TokenLength)
            {
                throw new AdbProtocolException("The ADB peer sent an invalid authentication challenge.");
            }

            AdbIdentity identity;
            try
            {
                identity = _identities.GetIdentity();
                await WriteAsync(
                    stream,
                    AdbAuthentication.SignatureMessage(identity, reply.Payload),
                    deadline).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or CryptographicException)
            {
                return (new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null), 0);
            }

            reply = await ReadAsync(stream, deadline).ConfigureAwait(false);
            if (reply.Command is AdbCommand.Auth)
            {
                if (reply.Arg0 != AdbAuthentication.TokenType
                    || reply.Payload.Length != AdbAuthentication.TokenLength)
                {
                    throw new AdbProtocolException(
                        "The ADB peer sent an invalid follow-up authentication challenge.");
                }

                await WriteAsync(
                    stream,
                    AdbAuthentication.PublicKeyMessage(identity),
                    deadline).ConfigureAwait(false);

                using var authorizationTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
                authorizationTimeout.CancelAfter(AuthorizationPromptTimeout);
                try
                {
                    reply = await ReadAsync(stream, authorizationTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!callerCancellation.IsCancellationRequested)
                {
                    return (new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null), 0);
                }
                catch (IOException)
                {
                    return (new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null), 0);
                }

                if (reply.Command is AdbCommand.Auth)
                {
                    return (new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null), 0);
                }
            }
        }

        if (reply.Command is not AdbCommand.Connect)
        {
            throw new AdbProtocolException($"Unexpected ADB command {reply.Command} during connect.");
        }

        var banner = AdbBanner.Parse(Encoding.UTF8.GetString(reply.Payload));
        // An older adbd announces 4096 here; a zero or absurd value gets the smallest safe chunk.
        var remoteMaxData = reply.Arg1 is > 0 and <= AdbMessage.MaxPayloadLength ? reply.Arg1 : 4096u;
        return (new AdbProbeResult(AdbConnectionState.Connected, port, banner), remoteMaxData);
    }

    private static void ValidatePort(int port)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
    }

    private static bool IsAndroidComponentName(string value) =>
        value.Length > 0
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_');

    /// <summary>
    /// Opens one ADB service stream, feeds it <paramref name="payload"/> if there is one, and
    /// returns everything the service wrote back.
    /// </summary>
    /// <remarks>
    /// ADB streams are flow-controlled one message at a time: every WRTE the host sends must be
    /// answered with OKAY before the next may go, and every WRTE the device sends is acknowledged
    /// the same way. The device may close the stream at any point; whatever it wrote before that is
    /// the answer.
    /// </remarks>
    private static async Task<string> RunServiceAsync(
        NetworkStream stream,
        string destination,
        ReadOnlyMemory<byte>? payload,
        int chunkLength,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        const uint localId = 1;
        await WriteAsync(
            stream,
            new AdbMessage(AdbCommand.Open, localId, 0, Encoding.UTF8.GetBytes($"{destination}\0")),
            cancellationToken).ConfigureAwait(false);

        uint remoteId = 0;
        var sent = 0;
        var awaitingOkay = true;
        var closed = false;
        using var output = new MemoryStream();
        while (!closed)
        {
            var message = await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (message.Arg1 != localId && message.Arg1 != 0)
            {
                continue;
            }

            switch (message.Command)
            {
                case AdbCommand.Okay:
                    remoteId = message.Arg0;
                    awaitingOkay = false;
                    break;

                case AdbCommand.Write:
                    remoteId = remoteId == 0 ? message.Arg0 : remoteId;
                    if (output.Length + message.Payload.Length > AdbMessage.MaxPayloadLength)
                    {
                        throw new AdbProtocolException("ADB service output exceeded the safety cap.");
                    }

                    await output.WriteAsync(message.Payload, cancellationToken).ConfigureAwait(false);
                    await WriteAsync(
                        stream,
                        new AdbMessage(AdbCommand.Okay, localId, message.Arg0, []),
                        cancellationToken).ConfigureAwait(false);
                    break;

                case AdbCommand.Close:
                    closed = true;
                    if (remoteId != 0)
                    {
                        await WriteAsync(
                            stream,
                            new AdbMessage(AdbCommand.Close, localId, remoteId, []),
                            cancellationToken).ConfigureAwait(false);
                    }

                    break;

                default:
                    throw new AdbProtocolException(
                        $"Unexpected ADB command {message.Command} in the service stream.");
            }

            if (closed || awaitingOkay || payload is not { } bytes || sent >= bytes.Length)
            {
                continue;
            }

            var chunk = bytes.Slice(sent, Math.Min(chunkLength, bytes.Length - sent));
            await WriteAsync(
                stream,
                new AdbMessage(AdbCommand.Write, localId, remoteId, chunk.ToArray()),
                cancellationToken).ConfigureAwait(false);
            sent += chunk.Length;
            awaitingOkay = true;
            progress?.Report((double)sent / bytes.Length);
        }

        return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
    }

    private static async Task WriteAsync(
        NetworkStream stream,
        AdbMessage message,
        CancellationToken cancellationToken)
    {
        var bytes = message.ToBytes();
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AdbMessage> ReadAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var headerBytes = new byte[AdbMessage.HeaderLength];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        var header = AdbMessage.ParseHeader(headerBytes);
        var payload = header.PayloadLength == 0 ? [] : new byte[header.PayloadLength];
        if (payload.Length > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        return new AdbMessage(header.Command, header.Arg0, header.Arg1, payload);
    }
}
