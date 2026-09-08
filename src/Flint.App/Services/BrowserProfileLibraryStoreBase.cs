namespace Flint.App.Services;

/// <summary>Shared validated, bounded mutation semantics for persistent and ephemeral stores.</summary>
public abstract partial class BrowserProfileLibraryStoreBase : IBrowserProfileLibraryStore
{
    private readonly object gate = new();
    private readonly Dictionary<BrowserProfileId, BrowserProfileLibraryState> profiles = [];

    /// <inheritdoc />
    public BrowserSavedSession Session(BrowserProfileId profile)
    {
        lock (gate) return (Get(profile).Session ?? BrowserSavedSession.Empty).Normalize();
    }

    /// <inheritdoc />
    public bool SaveSession(BrowserProfileId profile, BrowserSavedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var safe = session.Normalize();
        lock (gate)
        {
            var current = Get(profile);
            if (safe.SameAs(current.Session ?? BrowserSavedSession.Empty)) return true;
            return Commit(profile, current with { Session = safe });
        }
    }

    /// <inheritdoc />
    public BrowserProfileLibrarySnapshot Snapshot(BrowserProfileId profile)
    {
        lock (gate)
        {
            return Get(profile).ToSnapshot();
        }
    }

    /// <inheritdoc />
    public bool AddBookmark(
        BrowserProfileId profile,
        string url,
        string title,
        long faviconId = 0,
        DateTimeOffset? visitedAt = null)
    {
        var entry = BrowserProfileLibraryPolicy.CreateEntry(url, title, faviconId, visitedAt);
        if (entry is null)
        {
            return false;
        }

        lock (gate)
        {
            var current = Get(profile);
            var next = current with
            {
                Bookmarks = PrependDistinct(
                    entry,
                    current.Bookmarks,
                    BrowserProfileLibraryLimits.MaxBookmarks),
            };
            return Commit(profile, next);
        }
    }

    /// <inheritdoc />
    public bool RemoveBookmark(BrowserProfileId profile, string url)
    {
        var canonical = BrowserProfileLibraryPolicy.CanonicalizeUrl(url);
        if (canonical is null)
        {
            return false;
        }

        lock (gate)
        {
            var current = Get(profile);
            var nextBookmarks = current.Bookmarks
                .Where(entry => !StringComparer.Ordinal.Equals(entry.Url, canonical))
                .ToArray();
            return nextBookmarks.Length != current.Bookmarks.Length
                && Commit(profile, current with { Bookmarks = nextBookmarks });
        }
    }

    /// <inheritdoc />
    public bool RecordVisit(
        BrowserProfileId profile,
        string url,
        string title,
        long faviconId = 0,
        DateTimeOffset? visitedAt = null)
    {
        var entry = BrowserProfileLibraryPolicy.CreateEntry(url, title, faviconId, visitedAt);
        if (entry is null)
        {
            return false;
        }

        lock (gate)
        {
            var current = Get(profile);
            var next = current with
            {
                History = PrependDistinct(
                    entry,
                    current.History,
                    BrowserProfileLibraryLimits.MaxHistoryEntries),
            };
            return Commit(profile, next);
        }
    }

    /// <inheritdoc />
    public bool UpdateFavicon(BrowserProfileId profile, string url, long faviconId)
    {
        var canonical = BrowserProfileLibraryPolicy.CanonicalizeUrl(url);
        if (canonical is null || faviconId < 0)
        {
            return false;
        }

        lock (gate)
        {
            var current = Get(profile);
            var changed = false;
            var bookmarks = Update(current.Bookmarks, canonical, faviconId, ref changed);
            var history = Update(current.History, canonical, faviconId, ref changed);
            return changed && Commit(profile, current with { Bookmarks = bookmarks, History = history });
        }
    }

    /// <inheritdoc />
    public bool ClearBookmarks(BrowserProfileId profile)
    {
        lock (gate)
        {
            var current = Get(profile);
            return Commit(profile, current with { Bookmarks = [] });
        }
    }

    /// <inheritdoc />
    public bool ClearHistory(BrowserProfileId profile)
    {
        lock (gate)
        {
            var current = Get(profile);
            return Commit(profile, current with { History = [] });
        }
    }

    /// <inheritdoc />
    public bool ClearAll(BrowserProfileId profile)
    {
        lock (gate)
        {
            return Commit(profile, Get(profile) with { Bookmarks = [], History = [], Session = null });
        }
    }

    /// <summary>Loads and validates one profile when it is first used by this store instance.</summary>
    protected abstract BrowserProfileLibraryState Load(BrowserProfileId profile);

    /// <summary>Durably commits the next state. Failure must leave the previous file intact.</summary>
    protected abstract bool TryPersist(BrowserProfileId profile, BrowserProfileLibraryState state);

    private BrowserProfileLibraryState Get(BrowserProfileId profile)
    {
        if (!profiles.TryGetValue(profile, out var state))
        {
            state = Load(profile);
            profiles.Add(profile, state);
        }

        return state;
    }

    private bool Commit(BrowserProfileId profile, BrowserProfileLibraryState next)
    {
        if (!TryPersist(profile, next))
        {
            return false;
        }

        profiles[profile] = next;
        return true;
    }

    private static BrowserProfileLibraryEntry[] PrependDistinct(
        BrowserProfileLibraryEntry entry,
        IReadOnlyList<BrowserProfileLibraryEntry> current,
        int maximum)
    {
        var candidates = new List<BrowserProfileLibraryEntry>(current.Count + 1) { entry };
        candidates.AddRange(current);
        var result = new List<BrowserProfileLibraryEntry>(Math.Min(candidates.Count, maximum));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates.OrderByDescending(candidate => candidate.LastVisitedMilliseconds))
        {
            if (result.Count == maximum)
            {
                break;
            }

            if (seen.Add(candidate.Url))
            {
                result.Add(candidate);
            }
        }

        return [.. result];
    }

    private static BrowserProfileLibraryEntry[] Update(
        IReadOnlyList<BrowserProfileLibraryEntry> entries,
        string canonicalUrl,
        long faviconId,
        ref bool changed)
    {
        BrowserProfileLibraryEntry[]? copy = null;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!StringComparer.Ordinal.Equals(entry.Url, canonicalUrl) || entry.FaviconId == faviconId)
            {
                continue;
            }

            copy ??= entries.ToArray();
            copy[index] = entry with { FaviconId = faviconId };
            changed = true;
        }

        return copy ?? entries.ToArray();
    }
}

/// <summary>Internal immutable representation; arrays are never handed to callers.</summary>
public sealed record BrowserProfileLibraryState(
    BrowserProfileLibraryEntry[] Bookmarks,
    BrowserProfileLibraryEntry[] History,
    BrowserSavedSession? Session = null,
    Dictionary<Guid, string>? DeviceProfiles = null,
    Guid? SelectedDeviceProfile = null)
{
    /// <summary>The canonical empty internal state.</summary>
    public static BrowserProfileLibraryState Empty { get; } = new([], []);

    /// <summary>Copies this state for a consumer.</summary>
    public BrowserProfileLibrarySnapshot ToSnapshot() =>
        Bookmarks.Length == 0 && History.Length == 0
            ? BrowserProfileLibrarySnapshot.Empty
            : new BrowserProfileLibrarySnapshot(Bookmarks, History);
}
