using Avalonia.Media.Imaging;
using Flint.Protocol;

namespace Flint.App.Services;

/// <summary>Latest-only JPEG decoder for the optional browser preview.</summary>
/// <remarks>
/// The TLS receive loop must never wait for image decode, and stale frames must never queue behind a
/// slow machine. At most one frame is decoding and one is waiting; a newer waiting frame replaces
/// the older one and increments the visible dropped count.
/// </remarks>
internal sealed class BrowserPreviewDecoder(
    IBrowserUiDispatcher dispatcher,
    Action<BrowserPreviewMessage, Bitmap> onDecoded,
    Action onDropped,
    Action<Exception> onFailure) : IDisposable
{
    private readonly object gate = new();
    private BrowserPreviewMessage? pending;
    private bool workerRunning;
    private bool disposed;

    public void Offer(BrowserPreviewMessage frame)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (pending is not null)
            {
                dispatcher.Dispatch(onDropped);
            }
            pending = frame;
            if (workerRunning)
            {
                return;
            }
            workerRunning = true;
        }

        _ = Task.Run(ProcessAsync);
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            pending = null;
        }
    }

    private async Task ProcessAsync()
    {
        while (true)
        {
            BrowserPreviewMessage? frame;
            lock (gate)
            {
                if (disposed || pending is null)
                {
                    workerRunning = false;
                    return;
                }
                frame = pending;
                pending = null;
            }

            try
            {
                await using var stream = new MemoryStream(frame.Jpeg.ToArray(), writable: false);
                var bitmap = new Bitmap(stream);
                dispatcher.Dispatch(() =>
                {
                    lock (gate)
                    {
                        if (disposed)
                        {
                            bitmap.Dispose();
                            return;
                        }
                    }
                    onDecoded(frame, bitmap);
                });
            }
            catch (Exception exception) when (exception is ArgumentException or IOException)
            {
                dispatcher.Dispatch(() => onFailure(exception));
            }
        }
    }
}
