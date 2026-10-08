namespace Flint.Core;

/// <summary>An output Flint muted for a share, and whether it was muted before.</summary>
/// <param name="DeviceId">Windows' identity for the output.</param>
/// <param name="WasMuted">Whether it was already muted when Flint muted it.</param>
public sealed record MuteToRestore(string DeviceId, bool WasMuted);

/// <summary>What Flint remembers about this PC's sound outputs from one launch to the next.</summary>
public interface ISoundMemory
{
    /// <summary>An output muted for a share and not yet put back, or null.</summary>
    /// <remarks>
    /// Written before muting, so a Flint that is closed without putting it back, or killed, puts it
    /// back at the next launch.
    /// </remarks>
    MuteToRestore? PendingRestore { get; }

    /// <summary>Records <paramref name="restore"/> as waiting to be put back, or clears it with null.</summary>
    void SetPendingRestore(MuteToRestore? restore);

    /// <summary>Whether muting <paramref name="deviceId"/> was found to silence what Flint hears from it.</summary>
    bool MuteSilences(string deviceId);

    /// <summary>Remembers that muting <paramref name="deviceId"/> silences what Flint hears from it.</summary>
    void RememberMuteSilences(string deviceId);
}

/// <summary>Sound memory that lasts only as long as the process, for tests and design-time shells.</summary>
public sealed class InMemorySoundMemory : ISoundMemory
{
    private readonly HashSet<string> silenced = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public MuteToRestore? PendingRestore { get; private set; }

    /// <inheritdoc />
    public void SetPendingRestore(MuteToRestore? restore) => PendingRestore = restore;

    /// <inheritdoc />
    public bool MuteSilences(string deviceId) => silenced.Contains(deviceId);

    /// <inheritdoc />
    public void RememberMuteSilences(string deviceId) => silenced.Add(deviceId);
}
