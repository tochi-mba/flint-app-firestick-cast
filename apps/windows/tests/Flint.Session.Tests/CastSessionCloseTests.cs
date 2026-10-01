using System.Net;
using System.Net.Sockets;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>
/// How a session ends, and that whoever is listening is told exactly once, with the reason.
/// </summary>
/// <remarks>
/// Before this, the session knew it had ended and nobody else did: the Cast page kept saying
/// "Connected" until the next thing it tried failed.
/// </remarks>
public sealed class CastSessionCloseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ATvThatSaysGoodbye_EndsTheSessionAsEndedByTheTv_WithItsWords()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();
        var raised = Record(session);

        await tv.SendAsync(new ByeMessage(ByeReason.ReceiverStopped, "The receiver is closing."));
        var closed = await session.WhenClosed.WaitAsync(Token);

        closed.ShouldBe(new CastSessionClosed(CastSessionEnd.EndedByTv, "The receiver is closing."));
        session.IsConnected.ShouldBeFalse();
        (await raised.Task.WaitAsync(Token)).ShouldBe(closed);
    }

    [Fact]
    public async Task AGoodbyeWithNoWords_HasNoDetail()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        await tv.SendAsync(new ByeMessage(ByeReason.Normal));

        (await session.WhenClosed.WaitAsync(Token)).ShouldBe(new CastSessionClosed(CastSessionEnd.EndedByTv));
    }

    [Fact]
    public async Task ATvThatHangsUpWithoutAGoodbye_IsALostConnection()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        tv.HangUp();

        (await session.WhenClosed.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ConnectionLost);
    }

    [Fact]
    public async Task AResetConnection_IsALostConnection()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        tv.Reset();

        (await session.WhenClosed.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ConnectionLost);
    }

    [Fact]
    public async Task AFrameThatCannotBeRead_IsAProtocolError()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();

        // A length below the envelope's own size is a frame no receiver could have written.
        await tv.SendRawAsync([0, 0, 0, 1, 0xFF]);

        (await session.WhenClosed.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ProtocolError);
    }

    [Fact]
    public async Task ClosingItHere_IsClosedByThisPc()
    {
        await using var tv = await FakeTv.StartAsync();
        var session = await tv.PairAsync();
        var raised = Record(session);

        await session.DisposeAsync();

        (await session.WhenClosed.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ClosedByThisPc);
        (await raised.Task.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ClosedByThisPc);
    }

    [Fact]
    public async Task ClosedIsRaisedExactlyOnce_HoweverManyWaysItEnds()
    {
        await using var tv = await FakeTv.StartAsync();
        var session = await tv.PairAsync();
        var raised = 0;
        session.Closed += _ => Interlocked.Increment(ref raised);

        await tv.SendAsync(new ByeMessage(ByeReason.Normal));
        await session.WhenClosed.WaitAsync(Token);
        await session.DisposeAsync();
        await session.DisconnectAsync(Token);

        raised.ShouldBe(1);
    }

    [Fact]
    public async Task AListenerThatArrivesLate_StillLearnsWhy()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();
        tv.HangUp();
        await session.WhenClosed.WaitAsync(Token);

        // Awaited after the end: completes at once with what was recorded.
        var late = session.WhenClosed;

        late.IsCompleted.ShouldBeTrue();
        (await late).Reason.ShouldBe(CastSessionEnd.ConnectionLost);
    }

    [Fact]
    public async Task AListenerThatThrows_DoesNotStopTheOthersOrTheCleanup()
    {
        await using var tv = await FakeTv.StartAsync();
        await using var session = await tv.PairAsync();
        session.Closed += _ => throw new InvalidOperationException("a listener's own bug");
        var second = Record(session);

        await tv.SendAsync(new ByeMessage(ByeReason.Normal));

        (await second.Task.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.EndedByTv);
        session.IsConnected.ShouldBeFalse();
        await Should.ThrowAsync<IOException>(() => session.SendControlAsync(
            new ControlMessage(1, new KeyControl(KeyAction.Down, 23)), Token));
    }

    [Fact]
    public async Task Disconnect_SaysGoodbyeBeforeClosing()
    {
        await using var tv = await FakeTv.StartAsync();
        var session = await tv.PairAsync();

        await session.DisconnectAsync(Token);

        var goodbye = (await tv.ReadAsync()).Message.ShouldBeOfType<ByeMessage>();
        goodbye.Reason.ShouldBe(ByeReason.Normal);
        (await tv.ReadsEndAsync()).ShouldBeTrue("the socket closes after the goodbye");
        (await session.WhenClosed.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ClosedByThisPc);
    }

    [Fact]
    public async Task Disconnect_IsSafeTwice_AndOnASessionTheTvAlreadyClosed()
    {
        await using var tv = await FakeTv.StartAsync();
        var session = await tv.PairAsync();
        tv.HangUp();
        await session.WhenClosed.WaitAsync(Token);

        await Should.NotThrowAsync(() => session.DisconnectAsync(Token));
        await Should.NotThrowAsync(() => session.DisconnectAsync(Token));
        (await session.WhenClosed).Reason.ShouldBe(CastSessionEnd.ConnectionLost, "the TV went first");
    }

    [Fact]
    public async Task Disconnect_WhenTheGoodbyeCannotBeSent_StillCloses()
    {
        await using var tv = await FakeTv.StartAsync();
        var session = await tv.PairAsync();
        using var abandoned = new CancellationTokenSource();
        await abandoned.CancelAsync();

        await session.DisconnectAsync(abandoned.Token);

        session.IsConnected.ShouldBeFalse();
        (await session.WhenClosed.WaitAsync(Token)).Reason.ShouldBe(CastSessionEnd.ClosedByThisPc);
    }

    private static TaskCompletionSource<CastSessionClosed> Record(CastSession session)
    {
        var raised = new TaskCompletionSource<CastSessionClosed>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Closed += closed => raised.TrySetResult(closed);
        return raised;
    }

    /// <summary>A receiver on loopback that pairs, then does whatever the test tells it to.</summary>
    private sealed class FakeTv : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private TcpClient? client;
        private NetworkStream? stream;

        private FakeTv(TcpListener listener) => this.listener = listener;

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
}
