using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Flint.Protocol;

namespace Flint.App.Tests;

/// <summary>
/// A receiver on loopback that accepts one pairing and then holds the session open.
/// </summary>
/// <remarks>
/// Enough for the Cast page to reach a connected session, which the mirror and media commands
/// require before they ever ask for the TV. It answers Hello and Auth and then only listens,
/// recording what it was sent, so a test that went on to stream is sending into silence rather
/// than into a real receiver. A test can also make it say goodbye or hang up, as a real TV does.
/// </remarks>
internal sealed class LoopbackReceiver : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource lifetime =
        CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly TaskCompletionSource<NetworkStream> paired = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<WireMessage> received = new();
    private readonly List<(Func<IReadOnlyCollection<WireMessage>, bool> Wanted, TaskCompletionSource Seen)> waiters = [];
    private TcpClient? client;

    internal LoopbackReceiver()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Completion = ServeAsync(lifetime.Token);
    }

    /// <summary>The port the Cast page pairs with.</summary>
    internal int Port { get; }

    /// <summary>Every message the session sent after pairing, in order.</summary>
    internal IReadOnlyCollection<WireMessage> Received => received;

    private Task Completion { get; }

    /// <summary>Says goodbye, as a TV whose Flint app is closing does.</summary>
    internal Task SendByeAsync(ByeReason reason = ByeReason.ReceiverStopped, string detail = "") =>
        SendAsync(new ByeMessage(reason, detail));

    /// <summary>Sends the session a message, as the TV would.</summary>
    internal async Task SendAsync(WireMessage message)
    {
        var stream = await paired.Task.WaitAsync(lifetime.Token);
        await stream.WriteAsync(WireCodec.Encode(new WireFrame(message)), lifetime.Token);
    }

    /// <summary>Hangs up without a goodbye, as a TV that lost power or Wi-Fi does.</summary>
    internal async Task CloseAsync()
    {
        await paired.Task.WaitAsync(lifetime.Token);
        client!.Close();
    }

    /// <summary>Completes when the session has sent a message of type <typeparamref name="T"/>.</summary>
    internal Task WaitForAsync<T>()
        where T : WireMessage => WaitUntilAsync(sent => sent.Any(message => message is T));

    /// <summary>Completes when what the session has sent satisfies <paramref name="condition"/>.</summary>
    internal Task WaitUntilAsync(Func<IReadOnlyCollection<WireMessage>, bool> condition)
    {
        lock (waiters)
        {
            if (condition(received))
            {
                return Task.CompletedTask;
            }

            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiters.Add((condition, seen));
            return seen.Task.WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
        }
    }

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

        client?.Dispose();
        lifetime.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        client = await listener.AcceptTcpClientAsync(cancellationToken);
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
        paired.TrySetResult(stream);

        while (true)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken);
            received.Enqueue(frame.Message);
            lock (waiters)
            {
                foreach (var waiter in waiters.Where(waiter => waiter.Wanted(received)).ToArray())
                {
                    waiter.Seen.TrySetResult();
                    waiters.Remove(waiter);
                }
            }
        }
    }

    private static async Task<WireFrame> ReadFrameAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var length = new byte[4];
        await stream.ReadExactlyAsync(length, cancellationToken);
        var bytes = new byte[4 + System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length)];
        length.CopyTo(bytes, 0);
        await stream.ReadExactlyAsync(bytes.AsMemory(4), cancellationToken);
        return WireCodec.Decode(bytes);
    }
}
