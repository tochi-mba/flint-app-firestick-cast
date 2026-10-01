namespace Flint.Core.Settings;

/// <summary>
/// The live settings, written to their store shortly after the last change.
/// </summary>
/// <remarks>
/// <para>
/// Writing waits for <see cref="SaveDelay"/> of quiet, because a slider dragged across its range
/// changes a value dozens of times a second and only the last one is worth keeping. Anything still
/// waiting is written by <see cref="Flush"/>, which the app calls as it exits.
/// </para>
/// <para>
/// Two writes never run at once, and whichever runs last writes the newest settings, so a slow
/// delayed write cannot land after the exit's write and put an older value back.
/// </para>
/// </remarks>
public sealed class SettingsService : ISettingsService, IDisposable
{
    /// <summary>How long settings must stay unchanged before they are written.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly IAppSettingsStore store;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Lock state = new();
    private readonly Lock writing = new();
    private AppSettings current;
    private CancellationTokenSource? pendingWrite;
    private bool unsaved;
    private bool disposed;

    /// <summary>Loads the settings from <paramref name="store"/>.</summary>
    /// <param name="store">Where settings are kept between runs.</param>
    /// <param name="delay">Waits before a write. Tests replace it so no test waits on a clock.</param>
    public SettingsService(IAppSettingsStore store, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.delay = delay ?? Task.Delay;
        current = (store.Load() ?? AppSettings.Default).Normalize();
    }

    /// <inheritdoc />
    public AppSettings Current
    {
        get
        {
            lock (state)
            {
                return current;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<SettingsChangedEventArgs>? Changed;

    /// <inheritdoc />
    public void Update(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        AppSettings previous;
        AppSettings next;
        CancellationToken token;
        lock (state)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            previous = current;
            next = (change(previous) ?? previous).Normalize();
            if (next == previous)
            {
                return;
            }

            current = next;
            unsaved = true;
            pendingWrite?.Cancel();
            pendingWrite?.Dispose();
            pendingWrite = new CancellationTokenSource();
            token = pendingWrite.Token;
        }

        _ = WriteLaterAsync(token);
        Changed?.Invoke(this, new SettingsChangedEventArgs(previous, next));
    }

    /// <summary>Writes any change still waiting, now.</summary>
    public void Flush()
    {
        lock (writing)
        {
            AppSettings toWrite;
            lock (state)
            {
                if (!unsaved)
                {
                    return;
                }

                unsaved = false;
                pendingWrite?.Cancel();
                toWrite = current;
            }

            store.Save(toWrite);
        }
    }

    /// <summary>Writes anything still waiting and stops accepting changes.</summary>
    public void Dispose()
    {
        Flush();
        lock (state)
        {
            disposed = true;
            pendingWrite?.Dispose();
            pendingWrite = null;
        }
    }

    private async Task WriteLaterAsync(CancellationToken token)
    {
        try
        {
            await delay(SaveDelay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            Flush();
        }
    }
}
