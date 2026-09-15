namespace Flint.App.Services;

/// <summary>Stable identity for one browser profile without embedding its display name.</summary>
public readonly record struct BrowserProfileId
{
    /// <summary>Creates a non-empty profile identity.</summary>
    public BrowserProfileId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A browser profile needs a non-empty identity.", nameof(value));
        }

        Value = value;
    }

    /// <summary>The opaque identity. It is safe to use in a local filename.</summary>
    public Guid Value { get; }
}

/// <summary>Storage limits chosen so every snapshot can be represented by the browser wire format.</summary>
public static class BrowserProfileLibraryLimits
{
    /// <summary>The TV UI remains useful without allowing an unbounded bookmark file.</summary>
    public const int MaxBookmarks = 200;

    /// <summary>The protocol carries collection counts in one byte.</summary>
    public const int MaxHistoryEntries = byte.MaxValue;

    /// <summary>Maximum UTF-8 bytes in a canonical URL.</summary>
    public const int MaxUrlBytes = 4 * 1024;

    /// <summary>Maximum UTF-8 bytes in a page title.</summary>
    public const int MaxTitleBytes = 512;

    /// <summary>Maximum encrypted bytes accepted from one on-disk profile.</summary>
    public const int MaxFileBytes = 4 * 1024 * 1024;
}

/// <summary>A bookmark or history row owned by the selected Windows profile.</summary>
public sealed record BrowserProfileLibraryEntry(
    string Url,
    string Title,
    long FaviconId,
    long LastVisitedMilliseconds);

/// <summary>An immutable point-in-time copy of a device-owned browser library.</summary>
public sealed record BrowserProfileLibrarySnapshot
{
    /// <summary>The canonical empty library.</summary>
    public static BrowserProfileLibrarySnapshot Empty { get; } = new([], []);

    /// <summary>Creates a defensive snapshot.</summary>
    public BrowserProfileLibrarySnapshot(
        IReadOnlyList<BrowserProfileLibraryEntry> bookmarks,
        IReadOnlyList<BrowserProfileLibraryEntry> history)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        ArgumentNullException.ThrowIfNull(history);
        Bookmarks = bookmarks.ToArray();
        History = history.ToArray();
    }

    /// <summary>Newest-first bookmarks.</summary>
    public IReadOnlyList<BrowserProfileLibraryEntry> Bookmarks { get; }

    /// <summary>Newest-first, canonical-URL-deduplicated history.</summary>
    public IReadOnlyList<BrowserProfileLibraryEntry> History { get; }
}

/// <summary>Local library operations for a profile that remains owned by this Windows device.</summary>
public interface IBrowserProfileLibraryStore
{
    BrowserDeviceProfilesSnapshot DeviceProfiles() => BrowserDeviceProfilesSnapshot.Default;
    BrowserProfileId? CreateDeviceProfile(string name) => null;
    bool SelectDeviceProfile(BrowserProfileId profile) => false;

    /// <summary>Loads both browsing modes for this profile.</summary>
    BrowserSavedSession Session(BrowserProfileId profile) => BrowserSavedSession.Empty;
    /// <summary>Atomically saves this profile's browsing modes.</summary>
    bool SaveSession(BrowserProfileId profile, BrowserSavedSession session) => false;

    /// <summary>Returns a defensive snapshot; invalid or unavailable persisted data appears empty.</summary>
    BrowserProfileLibrarySnapshot Snapshot(BrowserProfileId profile);

    /// <summary>Adds or refreshes one bookmark after validating its receiver-safe HTTPS URL.</summary>
    bool AddBookmark(
        BrowserProfileId profile,
        string url,
        string title,
        long faviconId = 0,
        DateTimeOffset? visitedAt = null);

    /// <summary>Removes one bookmark by canonical URL.</summary>
    bool RemoveBookmark(BrowserProfileId profile, string url);

    /// <summary>Adds or refreshes one history visit.</summary>
    bool RecordVisit(
        BrowserProfileId profile,
        string url,
        string title,
        long faviconId = 0,
        DateTimeOffset? visitedAt = null);

    /// <summary>Updates an already-known URL in bookmarks and history.</summary>
    bool UpdateFavicon(BrowserProfileId profile, string url, long faviconId);

    /// <summary>Clears this profile's bookmarks.</summary>
    bool ClearBookmarks(BrowserProfileId profile);

    /// <summary>Clears this profile's history.</summary>
    bool ClearHistory(BrowserProfileId profile);

    /// <summary>Clears all library data for this profile.</summary>
    bool ClearAll(BrowserProfileId profile);
}

/// <summary>Thread-safe ephemeral implementation used by tests and explicit guest sessions.</summary>
public sealed class InMemoryBrowserProfileLibraryStore : BrowserProfileLibraryStoreBase
{
    /// <inheritdoc />
    protected override BrowserProfileLibraryState Load(BrowserProfileId profile) =>
        BrowserProfileLibraryState.Empty;

    /// <inheritdoc />
    protected override bool TryPersist(BrowserProfileId profile, BrowserProfileLibraryState state) => true;
}
