using System.Net;
using System.Net.Sockets;
using Flint.Protocol;
using Shouldly;

namespace Flint.Session.Tests;

/// <summary>The login a TV grants at pairing: kept, presented in place of a code, and only to its own TV.</summary>
public sealed class CastSessionTokenTests
{
    private static readonly string Login = "Abc-_123" + new string('z', 35);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pairing_KeepsTheLoginTheTvGrants_WhenItIsWellFormed()
    {
        await using var tv = new HandshakeTv("Living Room", reply: _ => new AuthMessage(AuthMethod.SessionToken, Ascii(Login), "41234"));

        await using var session = await CastSession.ConnectAsync(IPAddress.Loopback, tv.Port, "123456", cancellationToken: Token);

        session.GrantedToken.ShouldBe(Login);
        session.BrowserSecureEndpointPort.ShouldBe(41234);
        (await tv.Presented).ShouldBe((AuthMethod.PairingCode, "123456"));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("has spaces in it and is long enough to be forty-three")]
    public async Task AReplyWithoutAWellFormedLogin_GrantsNone(string credential)
    {
        await using var tv = new HandshakeTv("Living Room", reply: _ => new AuthMessage(AuthMethod.SessionToken, Ascii(credential)));

        await using var session = await CastSession.ConnectAsync(IPAddress.Loopback, tv.Port, "123456", cancellationToken: Token);

        session.GrantedToken.ShouldBeNull("the session is made; it just has no login to keep");
    }

    [Fact]
    public async Task ALogin_IsPresentedInPlaceOfACode()
    {
        await using var tv = new HandshakeTv("Living Room", reply: _ => new AuthMessage(AuthMethod.SessionToken, Ascii(Login)));

        await using var session = await CastSession.ConnectWithTokenAsync(IPAddress.Loopback, tv.Port, Login, "Living Room", cancellationToken: Token);

        (await tv.Presented).ShouldBe((AuthMethod.SessionToken, Login));
        session.GrantedToken.ShouldBe(Login);
    }

    [Fact]
    public async Task ADifferentTv_IsNeverSentTheLogin()
    {
        await using var tv = new HandshakeTv("Bedroom", reply: _ => new AuthMessage(AuthMethod.SessionToken, Ascii(Login)));

        var mismatch = await Should.ThrowAsync<CastTvMismatchException>(() =>
            CastSession.ConnectWithTokenAsync(IPAddress.Loopback, tv.Port, Login, "Living Room", cancellationToken: Token));

        mismatch.Expected.ShouldBe("Living Room");
        mismatch.Answered.ShouldBe("Bedroom");
        mismatch.Message.ShouldNotContain(Login, Case.Sensitive);
        (await tv.Presented).ShouldBeNull("the connection closed before anything was presented");
    }

    [Fact]
    public async Task ARefusedLogin_IsToldApartFromOtherGoodbyes()
    {
        await using (var refusing = new HandshakeTv("Living Room", reply: _ => new ByeMessage(ByeReason.AuthenticationFailed, "Authentication failed")))
        {
            await Should.ThrowAsync<CastAuthenticationRejectedException>(() =>
                CastSession.ConnectWithTokenAsync(IPAddress.Loopback, refusing.Port, Login, "Living Room", cancellationToken: Token));
        }

        await using var stopping = new HandshakeTv("Living Room", reply: _ => new ByeMessage(ByeReason.ReceiverStopped, "Closing"));
        var other = await Should.ThrowAsync<WireFormatException>(() =>
            CastSession.ConnectWithTokenAsync(IPAddress.Loopback, stopping.Port, Login, "Living Room", cancellationToken: Token));
        other.ShouldNotBeOfType<CastAuthenticationRejectedException>();
        other.Message.ShouldBe("Receiver rejected the session: Closing");
    }

    [Fact]
    public async Task ADeviceThatTakesTheConnectionAndNeverAnswers_IsGivenUpOn()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;

        var started = System.Diagnostics.Stopwatch.StartNew();
        var failure = await Should.ThrowAsync<TimeoutException>(() =>
            CastSession.ConnectAsync(IPAddress.Loopback, port, "123456", cancellationToken: Token));

        started.Elapsed.ShouldBeGreaterThanOrEqualTo(CastSession.HandshakeTimeout - TimeSpan.FromSeconds(1));
        failure.Message.ShouldContain("did not finish connecting");
    }

    [Fact]
    public async Task CancellingAConnection_IsACancellation_NotATimeout()
    {
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CastSession.ConnectAsync(IPAddress.Loopback, port, "123456", cancellationToken: cancel.Token));
    }

    [Fact]
    public void ALoginHasTheShapeTheTvsLoginsHave()
    {
        CastSession.IsWellFormedToken(Login).ShouldBeTrue();
        CastSession.IsWellFormedToken(Login[..42]).ShouldBeFalse();
        CastSession.IsWellFormedToken(Login + "a").ShouldBeFalse();
        CastSession.IsWellFormedToken(Login[..42] + "=").ShouldBeFalse();
        CastSession.IsWellFormedToken(null).ShouldBeFalse();
        CastSession.TryReadGrantedToken(new AuthMessage(AuthMethod.PairingCode, Ascii(Login))).ShouldBeNull();
        CastSession.TryReadGrantedToken(new ByeMessage(ByeReason.Normal)).ShouldBeNull();
    }

    [Fact]
    public async Task ALogin_NeedsTheTvsNameAndTheLogin()
    {
        await Should.ThrowAsync<ArgumentException>(() =>
            CastSession.ConnectWithTokenAsync(IPAddress.Loopback, 47855, " ", "Living Room", cancellationToken: Token));
        await Should.ThrowAsync<ArgumentException>(() =>
            CastSession.ConnectWithTokenAsync(IPAddress.Loopback, 47855, Login, "", cancellationToken: Token));
    }

    private static BinaryData Ascii(string text) => BinaryData.From(System.Text.Encoding.ASCII.GetBytes(text));

    /// <summary>A TV on loopback that greets with a name and answers one credential as it is told.</summary>
    private sealed class HandshakeTv : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task serving;

        public HandshakeTv(string name, Func<AuthMessage, WireMessage> reply)
        {
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            serving = ServeAsync(name, reply);
        }

        public int Port { get; }

        /// <summary>The credential presented, or null when the session closed before presenting one.</summary>
        public Task<(AuthMethod Method, string Credential)?> Presented => presented.Task;

        private readonly TaskCompletionSource<(AuthMethod Method, string Credential)?> presented =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            try
            {
                await serving;
            }
            catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException or OperationCanceledException)
            {
            }

            listener.Dispose();
        }

        private async Task ServeAsync(string name, Func<AuthMessage, WireMessage> reply)
        {
            using var client = await listener.AcceptTcpClientAsync(Token);
            var stream = client.GetStream();
            await ReadAsync(stream);
            await WriteAsync(stream, new HelloMessage(1, 1, name, ValueList<CodecId>.From([CodecId.H264]), 1920, 1080, 320));
            try
            {
                var auth = (AuthMessage)(await ReadAsync(stream)).Message;
                presented.TrySetResult((auth.Method, System.Text.Encoding.ASCII.GetString(auth.Credential.Span)));
                await WriteAsync(stream, reply(auth));
            }
            catch (EndOfStreamException)
            {
                presented.TrySetResult(null);
            }
        }

        private static Task WriteAsync(NetworkStream stream, WireMessage message) =>
            stream.WriteAsync(WireCodec.Encode(new WireFrame(1, message)), Token).AsTask();

        private static async Task<WireFrame> ReadAsync(NetworkStream stream)
        {
            var length = new byte[4];
            await stream.ReadExactlyAsync(length, Token);
            var bytes = new byte[4 + System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(length)];
            length.CopyTo(bytes, 0);
            await stream.ReadExactlyAsync(bytes.AsMemory(4), Token);
            return WireCodec.Decode(bytes);
        }
    }
}
