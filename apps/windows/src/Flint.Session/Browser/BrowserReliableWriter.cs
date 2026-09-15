using System.Threading.Channels;
using Flint.Protocol;

namespace Flint.Session.Browser;

/// <summary>
/// Single-owner, bounded reliable writer for secure host-to-TV browser control traffic.
/// </summary>
/// <remarks>
/// The channel has a deliberately finite capacity and one writer task. It does not create a task
/// per socket write, so a stalled peer cannot build an unbounded send backlog. Receiver preview is
/// intentionally not sent by this host writer; its capacity-one policy belongs to the receiver.
/// </remarks>
internal sealed class BrowserReliableWriter : IAsyncDisposable
{
    internal const int ReliableCapacity = 32;

    private readonly Channel<PendingMessage> messages = Channel.CreateBounded<PendingMessage>(
        new BoundedChannelOptions(ReliableCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly CancellationTokenSource cancellation = new();
    private readonly int protocolVersion;
    private readonly Task writerLoop;
    private readonly Action<Exception> onFault;
    private int disposed;

    internal BrowserReliableWriter(Stream stream, int protocolVersion, Action<Exception> onFault)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (protocolVersion < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion), "Browser TLS requires protocol v2 or newer.");
        }

        this.protocolVersion = protocolVersion;
        this.onFault = onFault ?? throw new ArgumentNullException(nameof(onFault));
        writerLoop = RunAsync(stream);
    }

    internal async Task EnqueueAsync(WireMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfDisposed();
        var pending = new PendingMessage(message);
        await messages.Writer.WriteAsync(pending, cancellationToken).ConfigureAwait(false);
        await pending.Written.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void Fail(Exception exception)
    {
        messages.Writer.TryComplete(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        messages.Writer.TryComplete();
        cancellation.Cancel();
        try
        {
            await writerLoop.ConfigureAwait(false);
        }
        catch (Exception) when (Volatile.Read(ref disposed) != 0)
        {
            // The session's Completion task already carries a live writer failure. Disposal must
            // remain idempotent and must not obscure that earlier, observable failure.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task RunAsync(Stream stream)
    {
        Exception? failure = null;
        try
        {
            await foreach (var pending in messages.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
            {
                try
                {
                    await BrowserFrameStream.WriteAsync(stream, protocolVersion, pending.Message, cancellation.Token).ConfigureAwait(false);
                    pending.Written.TrySetResult();
                }
                catch (WireFormatException exception)
                {
                    // Local encode rejection (e.g. epoch=0 tab command) must fail that send only.
                    // Re-throwing used to tear down the TLS writer and crash Flint.App when a tab
                    // was clicked after reconnect before Windows had adopted the TV epoch.
                    pending.Written.TrySetException(exception);
                }
                catch (Exception exception)
                {
                    pending.Written.TrySetException(exception);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            while (messages.Reader.TryRead(out var pending))
            {
                pending.Written.TrySetCanceled(cancellation.Token);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            messages.Writer.TryComplete(exception);
            while (messages.Reader.TryRead(out var pending))
            {
                pending.Written.TrySetException(exception);
            }
            throw;
        }
        finally
        {
            if (failure is not null && Volatile.Read(ref disposed) == 0)
            {
                onFault(failure);
            }
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    private sealed class PendingMessage(WireMessage message)
    {
        internal WireMessage Message { get; } = message;
        internal TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
