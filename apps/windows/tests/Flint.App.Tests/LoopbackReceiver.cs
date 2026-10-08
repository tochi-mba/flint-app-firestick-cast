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
    private NetworkStream? current;

    internal LoopbackReceiver()
    {
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Completion = ServeAsync(lifetime.Token);
    }

    /// <summary>The port the Cast page pairs with.</summary>
    internal int Port { get; }

    /// <summary>The name the TV gives in its greeting.</summary>
    internal string Name { get; set; } = "Fire TV";

    /// <summary>The screen width the TV gives in its greeting.</summary>
    internal int ScreenWidth { get; set; } = 1920;

    /// <summary>
    /// The login the TV grants, and accepts in place of a code. Unset, it grants a word no store
    /// keeps, as the tests before logins did.
    /// </summary>
    internal string? Login { get; set; }

    /// <summary>The browser port the TV names in its login reply, or null for none.</summary>
    internal int? BrowserPort { get; set; }

    /// <summary>Whether the TV refuses every login, as one whose app restarted does.</summary>
    internal bool RefusesLogins { get; set; }

    /// <summary>How many sessions logged in with the TV's login rather than a code.</summary>
    internal int LoginsAccepted => loginsAccepted;

    /// <summary>How many login attempts the TV refused.</summary>
    internal int LoginsRefused => loginsRefused;

    private int loginsAccepted;
    private int loginsRefused;

    /// <summary>
    /// What the TV says when it is told to play a file. Unset, it says nothing and the test answers
    /// for it; <see cref="PlaysEverything"/> answers as a TV that plays whatever it is sent.
    /// </summary>
    internal Func<MediaCommandMessage, PlaybackStateMessage?>? AnswerLoad { get; set; }

    /// <summary>Answers every file with playing, from where it was asked to start, ninety seconds long.</summary>
    internal static PlaybackStateMessage PlaysEverything(MediaCommandMessage load) =>
        new(PlaybackState.Playing, load.StartPositionMs, 90_000);

    /// <summary>The files the TV was told to play, in order.</summary>
    internal IReadOnlyList<MediaCommandMessage> Loads =>
        [.. received.OfType<MediaCommandMessage>().Where(command => command.Action is MediaAction.Load)];

    /// <summary>Every message the session sent after pairing, in order.</summary>
    internal IReadOnlyCollection<WireMessage> Received => received;

    private Task Completion { get; }

    /// <summary>Says goodbye, as a TV whose Flint app is closing does.</summary>
    internal Task SendByeAsync(ByeReason reason = ByeReason.ReceiverStopped, string detail = "") =>
        SendAsync(new ByeMessage(reason, detail));

    /// <summary>Sends the session a message, as the TV would.</summary>
    internal async Task SendAsync(WireMessage message)
    {
        await paired.Task.WaitAsync(lifetime.Token);
        await current!.WriteAsync(WireCodec.Encode(new WireFrame(message)), lifetime.Token);
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
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException
            or ObjectDisposedException or InvalidOperationException)
        {
        }

        client?.Dispose();
        lifetime.Dispose();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        // Every session it is offered, side by side, as a TV does: the one the page pairs with, a
        // second pairing while the first is still open, and any made again with the TV's login.
        var sessions = new List<Task>();
        while (true)
        {
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is InvalidOperationException or SocketException
                or ObjectDisposedException or OperationCanceledException)
            {
                // The TV was put away: nothing more will connect, and what was open winds down.
                await Task.WhenAll(sessions.Select(EndQuietlyAsync));
                return;
            }

            sessions.Add(ServeOneAsync(client.GetStream(), cancellationToken));
        }
    }

    /// <summary>A session's end, whichever way it ended.</summary>
    private static async Task EndQuietlyAsync(Task session)
    {
        try
        {
            await session;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException or ObjectDisposedException
            or OperationCanceledException or SocketException or InvalidCastException)
        {
        }
    }

    private async Task ServeOneAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        await ReadFrameAsync(stream, cancellationToken);
        await stream.WriteAsync(
            WireCodec.Encode(new WireFrame(new HelloMessage(
                1,
                1,
                Name,
                ValueList<CodecId>.From([CodecId.H264]),
                ScreenWidth,
                1080,
                320))),
            cancellationToken);
        var auth = (AuthMessage)(await ReadFrameAsync(stream, cancellationToken)).Message;
        if (auth.Method is AuthMethod.SessionToken)
        {
            var presented = System.Text.Encoding.ASCII.GetString(auth.Credential.Span);
            if (RefusesLogins || presented != Login)
            {
                Interlocked.Increment(ref loginsRefused);
                await stream.WriteAsync(WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.AuthenticationFailed, "Authentication failed"))), cancellationToken);
                return;
            }

            Interlocked.Increment(ref loginsAccepted);
        }

        await stream.WriteAsync(
            WireCodec.Encode(new WireFrame(new AuthMessage(
                AuthMethod.SessionToken,
                BinaryData.From(System.Text.Encoding.ASCII.GetBytes(Login ?? "accepted")),
                BrowserPort?.ToString(System.Globalization.CultureInfo.InvariantCulture)))),
            cancellationToken);
        current = stream;
        paired.TrySetResult(stream);

        while (true)
        {
            var frame = await ReadFrameAsync(stream, cancellationToken);
            received.Enqueue(frame.Message);
            if (frame.Message is MediaCommandMessage { Action: MediaAction.Load } load)
            {
                // As a real Fire TV does: it says Buffering the moment it takes a new item.
                await stream.WriteAsync(WireCodec.Encode(new WireFrame(new PlaybackStateMessage(PlaybackState.Buffering))), cancellationToken);
                if (AnswerLoad?.Invoke(load) is { } answer)
                {
                    await stream.WriteAsync(WireCodec.Encode(new WireFrame(answer)), cancellationToken);
                }
            }

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
