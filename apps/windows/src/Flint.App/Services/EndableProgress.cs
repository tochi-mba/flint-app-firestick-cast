namespace Flint.App.Services;

/// <summary>
/// Progress that can be ended: a report still queued when <see cref="End"/> is called is dropped.
/// </summary>
/// <remarks>
/// <see cref="Progress{T}"/> posts each report to the context it was created on, so a report made
/// just before a send failed can run after the failure has been shown, and put "Sending… 90%" back
/// over "Media was not sent.". Ending this one first means nothing that was queued can overwrite
/// what actually happened. Where there is no UI thread to post to, reports run on the thread pool,
/// so ending also waits for a report that is already running.
/// </remarks>
/// <typeparam name="T">The value reported.</typeparam>
internal sealed class EndableProgress<T> : IProgress<T>
{
    private readonly SynchronizationContext context;
    private readonly Action<T> handler;
    private readonly Lock gate = new();
    private bool ended;

    /// <summary>Creates progress that runs <paramref name="handler"/> on the current context.</summary>
    public EndableProgress(Action<T> handler)
    {
        this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        context = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    /// <inheritdoc />
    public void Report(T value) => context.Post(_ => Show(value), null);

    /// <summary>
    /// Stops every report, including those already queued, from reaching the handler. Returns once
    /// none is running.
    /// </summary>
    public void End()
    {
        lock (gate)
        {
            ended = true;
        }
    }

    private void Show(T value)
    {
        lock (gate)
        {
            if (!ended)
            {
                handler(value);
            }
        }
    }
}
