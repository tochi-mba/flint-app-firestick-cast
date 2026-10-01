using System.Net;
using System.Net.Sockets;
using Flint.Protocol;

namespace Flint.Session.Tests;

/// <summary>A receiver on loopback that pairs, then does whatever the test tells it to.</summary>
internal sealed class FakeTv : IAsyncDisposable
{
    private readonly TcpListener listener;
    private TcpClient? client;
    private NetworkStream? stream;

    private FakeTv(TcpListener listener) => this.listener = listener;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static Task<FakeTv> StartAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new FakeTv(listener));
    }

    public async Task<CastSession> PairAsync()
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var answering = Task.Run(async () =>
        {
            client = await listener.AcceptTcpClientAsync(Token);
            stream = client.GetStream();
            await ReadAsync();
            await SendAsync(new HelloMessage(1, 1, "Fire TV", ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320));
            await ReadAsync();
            await SendAsync(new AuthMessage(AuthMethod.SessionToken, BinaryData.From("accepted"u8)));
        }, Token);
        var session = await CastSession.ConnectAsync(IPAddress.Loopback, port, "123456", cancellationToken: Token);
        await answering;
        return session;
    }

    public Task SendAsync(WireMessage message) =>
        stream!.WriteAsync(WireCodec.Encode(new WireFrame(1, message)), Token).AsTask();

    public Task SendRawAsync(byte[] bytes) => stream!.WriteAsync(bytes, Token).AsTask();

    public async Task<WireFrame> ReadAsync()
    {
        var length = new byte[4];
        await stream!.ReadExactlyAsync(length, Token);
        var body = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length);
        var bytes = new byte[body + 4];
        length.CopyTo(bytes, 0);
        await stream.ReadExactlyAsync(bytes.AsMemory(4), Token);
        return WireCodec.Decode(bytes);
    }

    /// <summary>Whether the session's side of the socket has closed.</summary>
    public async Task<bool> ReadsEndAsync() => await stream!.ReadAsync(new byte[1], Token) == 0;

    public void HangUp() => client!.Close();

    public void Reset()
    {
        client!.Client.LingerState = new LingerOption(true, 0);
        client.Close();
    }

    public ValueTask DisposeAsync()
    {
        client?.Dispose();
        listener.Stop();
        listener.Dispose();
        return ValueTask.CompletedTask;
    }
}
