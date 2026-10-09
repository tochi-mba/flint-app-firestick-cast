namespace Flint.Platform.Windows;

/// <summary>Owns Flint's per-desktop-session single-instance lock.</summary>
/// <remarks>
/// The installer replaces one installed directory atomically, but a portable copy can live beside
/// it. The lock gives every copy the same identity, so launching an old extraction cannot leave two
/// Flint shells competing for the television, logs, and profile store. A second launch asks the
/// first to bring its window forward through a second named event, rather than doing nothing.
/// </remarks>
public sealed class FlintSingleInstance : IDisposable
{
    private const string Name = @"Local\REX-Technologies-Flint-Desktop";
    private const string ShowSuffix = "-Show";
    private readonly EventWaitHandle identity;
    private readonly string name;
    private EventWaitHandle? showRequests;
    private RegisteredWaitHandle? listening;
    private bool ownsIdentity;

    private FlintSingleInstance(EventWaitHandle identity, string name)
    {
        this.identity = identity;
        this.name = name;
        ownsIdentity = true;
    }

    /// <summary>Acquires Flint's process-wide identity, or returns null when another copy owns it.</summary>
    public static FlintSingleInstance? TryAcquire() => TryAcquire(Name);

    internal static FlintSingleInstance? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var candidate = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.ManualReset,
            name: name,
            createdNew: out var createdNew);
        if (createdNew)
        {
            return new FlintSingleInstance(candidate, name);
        }

        candidate.Dispose();
        return null;
    }

    /// <summary>Asks the copy already running to bring its window forward.</summary>
    /// <returns>Whether there was one listening to ask.</returns>
    public static bool AskRunningCopyToShow() => AskToShow(Name);

    internal static bool AskToShow(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name + ShowSuffix, out var requests))
        {
            return false;
        }

        using (requests)
        {
            return requests.Set();
        }
    }

    /// <summary>Calls <paramref name="onShow"/>, on a pool thread, each time a second launch asks this copy to show itself.</summary>
    public void WhenAskedToShow(Action onShow)
    {
        ArgumentNullException.ThrowIfNull(onShow);
        listening?.Unregister(null);
        showRequests ??= new EventWaitHandle(false, EventResetMode.AutoReset, name + ShowSuffix);
        listening = ThreadPool.RegisterWaitForSingleObject(showRequests, (_, _) => onShow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        listening?.Unregister(null);
        listening = null;
        showRequests?.Dispose();
        showRequests = null;
        if (!ownsIdentity)
        {
            return;
        }

        // The named kernel object exists exactly while its owner's handle does. A crash closes the
        // handle too, so no stale lock can strand a later launch, and unlike a mutex this handle can
        // safely be disposed after an async desktop lifetime resumes on a different thread.
        ownsIdentity = false;
        identity.Dispose();
    }
}
