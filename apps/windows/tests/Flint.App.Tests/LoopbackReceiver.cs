using System.Net;
using System.Net.Sockets;
using Flint.Protocol;

namespace Flint.App.Tests;

/// <summary>
/// A receiver on loopback that accepts one pairing and then holds the session open.
/// </summary>
/// <remarks>
/// Enough for the Cast page to reach a connected session, which the mirror and media commands
/// require before they ever ask for the TV. It answers Hello and Auth and then only listens, so
/// a test that went on to stream would be sending into silence rather than into a real receiver.
/// </remarks>
internal sealed class LoopbackReceiver : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime =
        CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    internal LoopbackReceiver()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Completion = ServeAsync(lifetime.Token);
    }

    /// <summary>The port the Cast page pairs with.</summary>
    internal int Port { get; }

    private Task Completion { get; }

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        listener.Stop();
        try
        {
            await Completion;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
        }

        lifetime.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        var stream = client.GetStream();
        await ReadFrameAsync(stream, cancellationToken);
        await stream.WriteAsync(
            WireCodec.Encode(new WireFrame(new HelloMessage(
                1,
                1,
                "Fire TV",
                ValueList<CodecId>.From([CodecId.H264]),
                1920,
                1080,
                320))),
            cancellationToken);
        await ReadFrameAsync(stream, cancellationToken);
        await stream.WriteAsync(
            WireCodec.Encode(new WireFrame(new AuthMessage(AuthMethod.SessionToken, BinaryData.From("accepted"u8)))),
            cancellationToken);

        var sink = new byte[4096];
        while (await stream.ReadAsync(sink, cancellationToken) > 0)
        {
        }
    }

    private static async Task ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellationToken);
        var body = new byte[System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length)];
        await stream.ReadExactlyAsync(body, cancellationToken);
    }
}
