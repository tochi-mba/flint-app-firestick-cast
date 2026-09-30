using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Flint.Discovery.Tests;

/// <summary>
/// An already-authorised ADB device on loopback that answers services from a script.
/// </summary>
/// <remarks>
/// It greets with CNXN straight away, as a television that has already accepted Flint's key does,
/// and then serves one stream at a time: OKAY to the OPEN, the scripted answer as WRTE, then CLSE.
/// A service that takes a payload is fed every WRTE the host sends, acknowledging each one, so the
/// flow control the real protocol demands is exercised rather than assumed.
/// </remarks>
internal sealed class FakeAdbDevice : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime =
        CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly Func<string, ReadOnlyMemory<byte>, string> _services;
    private readonly uint _maxData;
    private readonly uint _announcedMaxData;
    private readonly List<string> _requests = [];

    /// <param name="services">
    /// Answers a service destination (such as <c>shell:getprop</c>) and the payload the host sent it.
    /// </param>
    /// <param name="maxData">The largest message the device accepts; a larger one fails the test.</param>
    /// <param name="announcedMaxData">
    /// What the device tells the host it accepts, when that differs from what it enforces. A device
    /// that announces nothing useful (zero) is how older adbd builds behave.
    /// </param>
    internal FakeAdbDevice(
        Func<string, ReadOnlyMemory<byte>, string> services,
        uint maxData = 64 * 1024,
        uint? announcedMaxData = null)
    {
        _services = services;
        _maxData = maxData;
        _announcedMaxData = announcedMaxData ?? maxData;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _lifetime.CancelAfter(TimeSpan.FromSeconds(20));
        Completion = ServeAsync(_lifetime.Token);
    }

    internal int Port { get; }

    internal Task Completion { get; }

    /// <summary>Every service the host opened, in order.</summary>
    internal IReadOnlyList<string> Requests => _requests;

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

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var peer = await _listener.AcceptTcpClientAsync(cancellationToken);
            var stream = peer.GetStream();
            try
            {
                var greeting = await ReadAsync(stream, cancellationToken);
                if (greeting.Command is not AdbCommand.Connect)
                {
                    return;
                }

                await WriteAsync(
                    stream,
                    new AdbMessage(AdbCommand.Connect, AdbMessage.ProtocolVersion, _announcedMaxData, "device::ro.product.model=AFTTEST\0"u8.ToArray()),
                    cancellationToken);

                while (true)
                {
                    var open = await ReadAsync(stream, cancellationToken);
                    if (open.Command is not AdbCommand.Open)
                    {
                        return;
                    }

                    var destination = Encoding.UTF8.GetString(open.Payload).TrimEnd('\0');
                    lock (_requests)
                    {
                        _requests.Add(destination);
                    }

                    await ServeStreamAsync(stream, open.Arg0, destination, cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or EndOfStreamException)
            {
                // The host hung up; the next test connection starts afresh.
            }
        }
    }

    private async Task ServeStreamAsync(NetworkStream stream, uint hostId, string destination, CancellationToken cancellationToken)
    {
        const uint deviceId = 77;
        await WriteAsync(stream, new AdbMessage(AdbCommand.Okay, deviceId, hostId, []), cancellationToken);

        var expected = ExpectedPayloadLength(destination);
        using var received = new MemoryStream();
        while (received.Length < expected)
        {
            var write = await ReadAsync(stream, cancellationToken);
            if (write.Command is AdbCommand.Close)
            {
                return;
            }

            if (write.Command is not AdbCommand.Write || write.Arg1 != deviceId)
            {
                throw new InvalidOperationException($"Expected WRTE on stream {deviceId}, got {write.Command}.");
            }

            if (write.Payload.Length > _maxData)
            {
                throw new InvalidOperationException($"A {write.Payload.Length}-byte WRTE exceeds the announced {_maxData}.");
            }

            received.Write(write.Payload);
            await WriteAsync(stream, new AdbMessage(AdbCommand.Okay, deviceId, hostId, []), cancellationToken);
        }

        var answer = Encoding.UTF8.GetBytes(_services(destination, received.ToArray()));
        await WriteAsync(stream, new AdbMessage(AdbCommand.Write, deviceId, hostId, answer), cancellationToken);
        var okay = await ReadAsync(stream, cancellationToken);
        if (okay.Command is not AdbCommand.Okay)
        {
            throw new InvalidOperationException($"Expected OKAY for the answer, got {okay.Command}.");
        }

        await WriteAsync(stream, new AdbMessage(AdbCommand.Close, deviceId, hostId, []), cancellationToken);
        var close = await ReadAsync(stream, cancellationToken);
        if (close.Command is not AdbCommand.Close)
        {
            throw new InvalidOperationException($"Expected CLSE back, got {close.Command}.");
        }
    }

    /// <summary>A streamed install says its size up front; every other service takes nothing.</summary>
    private static int ExpectedPayloadLength(string destination)
    {
        const string marker = " -S ";
        var at = destination.IndexOf(marker, StringComparison.Ordinal);
        return at < 0 ? 0 : int.Parse(destination[(at + marker.Length)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WriteAsync(NetworkStream stream, AdbMessage message, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(message.ToBytes(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<AdbMessage> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
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
}
