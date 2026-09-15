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
/// bypasses the prompt or assumes consent. Once authorised, it reads only Android build properties.
/// No package, setting, or file on the television is changed.
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
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

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
            return await HandshakeAsync(
                client.GetStream(),
                port,
                cancellationToken).ConfigureAwait(false);
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
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityName);
        if (!IsAndroidComponentName(packageName) || !IsAndroidComponentName(activityName))
        {
            throw new ArgumentException("The Android component name contains unsupported characters.");
        }

        using var client = new TcpClient(address.AddressFamily) { NoDelay = true };
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

        var command = $"am start -n {packageName}/{activityName}";
        var result = await HandshakeAsync(client.GetStream(), port, cancellationToken, command)
            .ConfigureAwait(false);
        if (result.State is not AdbConnectionState.Connected)
        {
            throw new IOException("The Fire TV did not authorize Flint to open the receiver.");
        }
    }

    private async Task<AdbProbeResult> HandshakeAsync(
        NetworkStream stream,
        int port,
        CancellationToken callerCancellation,
        string? shellCommand = null)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        handshakeTimeout.CancelAfter(HandshakeTimeout);
        var deadline = handshakeTimeout.Token;

        await WriteAsync(stream, AdbMessage.Connect(), deadline).ConfigureAwait(false);
        var reply = await ReadAsync(stream, deadline).ConfigureAwait(false);

        if (reply.Command is AdbCommand.StartTls)
        {
            return new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null);
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
                return new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null);
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
                    return new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null);
                }
                catch (IOException)
                {
                    return new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null);
                }

                if (reply.Command is AdbCommand.Auth)
                {
                    return new AdbProbeResult(AdbConnectionState.Unauthorized, port, Banner: null);
                }
            }
        }

        if (reply.Command is not AdbCommand.Connect)
        {
            throw new AdbProtocolException($"Unexpected ADB command {reply.Command} during connect.");
        }

        var banner = AdbBanner.Parse(Encoding.UTF8.GetString(reply.Payload));
        using var propertyTimeout =
            CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        propertyTimeout.CancelAfter(HandshakeTimeout);
        try
        {
            var output = await RunShellAsync(
                stream,
                shellCommand ?? "getprop",
                propertyTimeout.Token).ConfigureAwait(false);
            if (shellCommand is not null)
            {
                if (output.Contains("Error", StringComparison.OrdinalIgnoreCase))
                {
                    throw new AdbProtocolException($"Fire TV could not open the receiver: {output.Trim()}");
                }

                return new AdbProbeResult(AdbConnectionState.Connected, port, banner);
            }

            var properties = AdbBuildProperties.Parse(output);
            return new AdbProbeResult(
                AdbConnectionState.Connected,
                port,
                banner,
                properties.AndroidApiLevel,
                properties.AndroidRelease,
                properties.Model);
        }
        catch (OperationCanceledException) when (!callerCancellation.IsCancellationRequested)
        {
            return new AdbProbeResult(AdbConnectionState.Connected, port, banner);
        }
        catch (Exception exception) when (shellCommand is null
            && exception is IOException or AdbProtocolException)
        {
            return new AdbProbeResult(AdbConnectionState.Connected, port, banner);
        }
    }

    private static bool IsAndroidComponentName(string value) =>
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_');

    private static async Task<string> RunShellAsync(
        NetworkStream stream,
        string command,
        CancellationToken cancellationToken)
    {
        const uint localId = 1;
        var destination = Encoding.UTF8.GetBytes($"shell:{command}\0");
        await WriteAsync(
            stream,
            new AdbMessage(AdbCommand.Open, localId, 0, destination),
            cancellationToken).ConfigureAwait(false);

        uint remoteId = 0;
        using var output = new MemoryStream();
        while (true)
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
                    break;

                case AdbCommand.Write:
                    remoteId = remoteId == 0 ? message.Arg0 : remoteId;
                    if (output.Length + message.Payload.Length > AdbMessage.MaxPayloadLength)
                    {
                        throw new AdbProtocolException("ADB property output exceeded the safety cap.");
                    }

                    await output.WriteAsync(message.Payload, cancellationToken).ConfigureAwait(false);
                    await WriteAsync(
                        stream,
                        new AdbMessage(AdbCommand.Okay, localId, message.Arg0, []),
                        cancellationToken).ConfigureAwait(false);
                    break;

                case AdbCommand.Close:
                    if (remoteId != 0)
                    {
                        await WriteAsync(
                            stream,
                            new AdbMessage(AdbCommand.Close, localId, remoteId, []),
                            cancellationToken).ConfigureAwait(false);
                    }

                    return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));

                default:
                    throw new AdbProtocolException(
                        $"Unexpected ADB command {message.Command} in the property stream.");
            }
        }
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
