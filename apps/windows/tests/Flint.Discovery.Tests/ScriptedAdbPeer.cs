using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Flint.Discovery.Tests;

/// <summary>
/// One loopback connection whose device side follows a script, message by message.
/// </summary>
/// <remarks>
/// For the exchanges a well-behaved fake never produces: a television that asks for TLS, rejects
/// the key, goes quiet mid-prompt, or answers a service with something that is not ADB.
/// </remarks>
internal sealed class ScriptedAdbPeer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime =
        CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    internal ScriptedAdbPeer(Func<NetworkStream, CancellationToken, Task> script)
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _lifetime.CancelAfter(TimeSpan.FromSeconds(10));
        Completion = RunAsync(script, _lifetime.Token);
    }

    internal int Port { get; }

    internal Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        _lifetime.Cancel();
        try
        {
            await Completion;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }

        _lifetime.Dispose();
    }

    /// <summary>The device's greeting once authorised, announcing <paramref name="maxData"/>.</summary>
    internal static AdbMessage Banner(uint maxData = AdbMessage.MaxPayloadAdvertised) =>
        new(AdbCommand.Connect, AdbMessage.ProtocolVersion, maxData, Encoding.UTF8.GetBytes("device::ro.product.model=AFTKA\0"));

    /// <summary>An RSA challenge of the length the host expects.</summary>
    internal static AdbMessage Challenge() =>
        new(AdbCommand.Auth, AdbAuthentication.TokenType, 0, new byte[AdbAuthentication.TokenLength]);

    internal static async Task WriteAsync(NetworkStream stream, AdbMessage message, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(message.ToBytes(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static async Task<AdbMessage> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[AdbMessage.HeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var parsed = AdbMessage.ParseHeader(header);
        var payload = new byte[parsed.PayloadLength];
        if (payload.Length > 0)
        {
            await stream.ReadExactlyAsync(payload, cancellationToken);
        }

        return new AdbMessage(parsed.Command, parsed.Arg0, parsed.Arg1, payload);
    }

    /// <summary>Reads until the host goes away, so the host sees a peer that is simply silent.</summary>
    internal static async Task StaySilentAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var sink = new byte[256];
        while (await stream.ReadAsync(sink, cancellationToken) > 0)
        {
        }
    }

    private async Task RunAsync(Func<NetworkStream, CancellationToken, Task> script, CancellationToken cancellationToken)
    {
        using var peer = await _listener.AcceptTcpClientAsync(cancellationToken);
        await script(peer.GetStream(), cancellationToken);
    }
}
