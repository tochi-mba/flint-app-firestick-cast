namespace Flint.Core.Media;

/// <summary>A history that lasts only as long as the process.</summary>
/// <remarks>For tests and design-time shells, which must not read or write a person's real history.</remarks>
public sealed class InMemoryMediaHistoryStore : IMediaHistoryStore
{
    private readonly Dictionary<string, MediaHistoryEntry> entries = [];

    /// <summary>Every position kept, for tests to look at.</summary>
    public IReadOnlyCollection<MediaHistoryEntry> Entries => entries.Values;

    /// <inheritdoc />
    public MediaHistoryEntry? Find(string key) => entries.GetValueOrDefault(key);

    /// <inheritdoc />
    public void Save(MediaHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entries[entry.Key] = entry;
    }

    /// <inheritdoc />
    public void Forget(string key) => entries.Remove(key);

    /// <inheritdoc />
    public void Clear() => entries.Clear();
}
