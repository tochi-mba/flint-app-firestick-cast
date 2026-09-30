namespace Flint.Platform.Windows;

/// <summary>Owns Flint's per-desktop-session single-instance lock.</summary>
/// <remarks>
/// The installer replaces one installed directory atomically, but a portable copy can live beside
/// it. The lock gives every copy the same identity, so launching an old extraction cannot leave two
/// Flint shells competing for the television, logs, and profile store.
/// </remarks>
public sealed class FlintSingleInstance : IDisposable
{
    private const string Name = @"Local\REX-Technologies-Flint-Desktop";
    private readonly EventWaitHandle identity;
    private bool ownsIdentity;

    private FlintSingleInstance(EventWaitHandle identity)
    {
        this.identity = identity;
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
            return new FlintSingleInstance(candidate);
        }

        candidate.Dispose();
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
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
