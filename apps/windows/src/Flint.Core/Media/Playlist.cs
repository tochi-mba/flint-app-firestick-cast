using Flint.Core.Settings;

namespace Flint.Core.Media;

/// <summary>One file waiting to be played, or playing.</summary>
/// <param name="Id">The queue's own number for it, so two copies of one file stay apart.</param>
/// <param name="Path">Where the file is.</param>
/// <param name="Type">How it is sent, and what sort of thing it is.</param>
public sealed record PlaylistItem(long Id, string Path, MediaFileType Type)
{
    /// <summary>The file's name, as the queue shows it.</summary>
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// The files lined up to play on the TV, and which one is playing: the queue the Media page shows.
/// </summary>
/// <remarks>
/// <para>
/// The list order is what the person sees and arranges. Without shuffle it is also the playing
/// order: next is the item below the current one, previous the item above. With shuffle, every
/// item plays once in a random order before any plays again, previous goes back through what
/// actually played, and the list itself is left as the person arranged it.
/// </para>
/// <para>
/// Removing the playing item does not skip one: next is then whatever moved into its place. Pure:
/// no files are opened and nothing is sent, so every rule is tested without a TV.
/// </para>
/// </remarks>
public sealed class Playlist
{
    private readonly List<PlaylistItem> items = [];
    private readonly List<long> history = [];
    private readonly Random random;
    private List<long>? shuffleRemaining;

    /// <summary>
    /// The order of the next shuffled cycle, decided the first time anything looks past the end of
    /// this one, so the item shown as next is the item that then plays.
    /// </summary>
    private List<long>? nextCycle;
    private long nextId = 1;
    private long? currentId;

    /// <summary>Whether the playing item was removed, so nothing is playing but the place is kept.</summary>
    private bool removedCurrent;

    /// <summary>
    /// After the playing item was removed, the item that plays next: the one that followed it, or
    /// null when it was last. Held by identity, so moving items about does not lose the place.
    /// </summary>
    private long? afterRemoved;

    /// <summary>Creates an empty queue.</summary>
    /// <param name="random">Where shuffle orders come from; a fixed seed in tests.</param>
    public Playlist(Random? random = null) => this.random = random ?? Random.Shared;

    /// <summary>The items, in the order the person sees them.</summary>
    public IReadOnlyList<PlaylistItem> Items => items;

    /// <summary>The item playing, if any.</summary>
    public PlaylistItem? Current => currentId is { } id ? Find(id) : null;

    /// <summary>Whether the queue plays in a random order.</summary>
    public bool Shuffle
    {
        get => shuffleRemaining is not null;
        set
        {
            if (value == Shuffle)
            {
                return;
            }

            history.Clear();
            nextCycle = null;
            shuffleRemaining = value ? NewCycle() : null;
        }
    }

    /// <summary>Adds files to the end. Unplayable files are left out.</summary>
    /// <returns>The items added.</returns>
    public IReadOnlyList<PlaylistItem> Add(IEnumerable<string> paths)
    {
        var added = Create(paths);
        nextCycle = null;
        items.AddRange(added);
        foreach (var item in added)
        {
            // A new item takes its chance among those not yet played in this cycle.
            shuffleRemaining?.Insert(random.Next(shuffleRemaining.Count + 1), item.Id);
        }

        return added;
    }

    /// <summary>Adds files to play straight after the current one. Unplayable files are left out.</summary>
    /// <returns>The items added.</returns>
    public IReadOnlyList<PlaylistItem> AddNext(IEnumerable<string> paths)
    {
        var added = Create(paths);
        nextCycle = null;
        var at = currentId is { } id ? IndexOf(id) + 1 : removedCurrent ? NextPositionAfterRemoval : 0;
        items.InsertRange(at, added);
        if (removedCurrent && added.Count > 0)
        {
            afterRemoved = added[0].Id;
        }

        shuffleRemaining?.InsertRange(0, added.Select(item => item.Id));
        return added;
    }

    /// <summary>Removes an item. The playing item can be removed; next is then the one after it.</summary>
    /// <returns>Whether it was in the queue.</returns>
    public bool Remove(long id)
    {
        var index = IndexOf(id);
        if (index < 0)
        {
            return false;
        }

        items.RemoveAt(index);
        nextCycle = null;
        shuffleRemaining?.Remove(id);
        history.RemoveAll(played => played == id);
        if (id == currentId || (removedCurrent && id == afterRemoved))
        {
            // Whatever followed it now plays next.
            currentId = null;
            removedCurrent = true;
            afterRemoved = index < items.Count ? items[index].Id : null;
        }

        return true;
    }

    /// <summary>Moves an item to <paramref name="index"/> in the list, clamped to its ends.</summary>
    /// <returns>Whether it was in the queue.</returns>
    public bool Move(long id, int index)
    {
        var from = IndexOf(id);
        if (from < 0)
        {
            return false;
        }

        var item = items[from];
        items.RemoveAt(from);
        items.Insert(Math.Clamp(index, 0, items.Count), item);
        return true;
    }

    /// <summary>Empties the queue.</summary>
    public void Clear()
    {
        items.Clear();
        history.Clear();
        nextCycle = null;
        shuffleRemaining = Shuffle ? [] : null;
        currentId = null;
        removedCurrent = false;
        afterRemoved = null;
    }

    /// <summary>Makes <paramref name="id"/> the playing item, out of turn.</summary>
    /// <returns>The item, or null when it is not in the queue.</returns>
    public PlaylistItem? Select(long id)
    {
        var item = Find(id);
        if (item is null)
        {
            return null;
        }

        MoveTo(item);
        return item;
    }

    /// <summary>What <see cref="Advance"/> would play, without playing it.</summary>
    public PlaylistItem? PeekNext(RepeatMode repeat)
    {
        if (repeat is RepeatMode.One && Current is { } current)
        {
            return current;
        }

        if (shuffleRemaining is { } remaining)
        {
            if (remaining.Count > 0)
            {
                return Find(remaining[0]);
            }

            if (repeat is not RepeatMode.All || items.Count == 0)
            {
                return null;
            }

            nextCycle ??= RepeatCycle();
            return Find(nextCycle[0]);
        }

        var next = currentId is { } id ? IndexOf(id) + 1 : removedCurrent ? NextPositionAfterRemoval : 0;
        return next < items.Count ? items[next]
            : repeat is RepeatMode.All && items.Count > 0 ? items[0]
            : null;
    }

    /// <summary>Moves on to the next item.</summary>
    /// <returns>The item now playing, or null when the queue has ended.</returns>
    /// <remarks>
    /// Repeating one item returns the same item, which the caller plays again from the start
    /// rather than sending it again. With shuffle and repeat-all, a finished cycle starts a new one.
    /// </remarks>
    public PlaylistItem? Advance(RepeatMode repeat)
    {
        if (repeat is RepeatMode.One && Current is { } current)
        {
            return current;
        }

        if (shuffleRemaining is { Count: 0 } && repeat is RepeatMode.All && items.Count > 0)
        {
            shuffleRemaining = nextCycle ?? RepeatCycle();
            nextCycle = null;
        }

        var next = PeekNext(repeat);
        if (next is not null)
        {
            MoveTo(next);
        }

        return next;
    }

    /// <summary>Whether <see cref="GoBack"/> has somewhere to go.</summary>
    public bool CanGoBack => Shuffle
        ? history.Count > 0
        : (currentId is { } id ? IndexOf(id) : removedCurrent ? NextPositionAfterRemoval : 0) > 0;

    /// <summary>Goes back to the item before the current one.</summary>
    /// <returns>The item now playing, or null when there is nothing before it.</returns>
    public PlaylistItem? GoBack()
    {
        if (!CanGoBack)
        {
            return null;
        }

        if (Shuffle)
        {
            var previous = Find(history[^1])!;
            history.RemoveAt(history.Count - 1);
            if (currentId is { } leaving)
            {
                shuffleRemaining!.Insert(0, leaving);
            }

            shuffleRemaining!.Remove(previous.Id);
            currentId = previous.Id;
            removedCurrent = false;
            return previous;
        }

        var before = items[(currentId is { } id ? IndexOf(id) : NextPositionAfterRemoval) - 1];
        currentId = before.Id;
        removedCurrent = false;
        return before;
    }

    private void MoveTo(PlaylistItem item)
    {
        if (currentId is { } leaving && leaving != item.Id)
        {
            history.Add(leaving);
        }

        shuffleRemaining?.Remove(item.Id);
        currentId = item.Id;
        removedCurrent = false;
    }

    /// <summary>Where the item after a removed playing item now is: its list position, or the end.</summary>
    private int NextPositionAfterRemoval => afterRemoved is { } next ? IndexOf(next) : items.Count;

    private List<PlaylistItem> Create(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return
        [
            .. paths
                .Where(path => !string.IsNullOrWhiteSpace(path) && MediaFileTypes.IsPlayable(path))
                .Select(path => new PlaylistItem(nextId++, path, MediaFileTypes.For(path))),
        ];
    }

    /// <summary>A shuffled order of everything but the playing item, which has already had its turn.</summary>
    private List<long> NewCycle() => Shuffled(items.Select(item => item.Id).Where(id => id != currentId));

    /// <summary>
    /// A shuffled order of every item for the cycle after this one, never starting with the item
    /// that has just played: the same song twice in a row is the one order nobody wants from shuffle.
    /// </summary>
    private List<long> RepeatCycle()
    {
        var order = Shuffled(items.Select(item => item.Id));
        if (order.Count > 1 && order[0] == currentId)
        {
            var swapWith = 1 + random.Next(order.Count - 1);
            (order[0], order[swapWith]) = (order[swapWith], order[0]);
        }

        return order;
    }

    private List<long> Shuffled(IEnumerable<long> ids)
    {
        var order = ids.ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private PlaylistItem? Find(long id) => items.FirstOrDefault(item => item.Id == id);

    /// <summary>Where <paramref name="id"/> is in the list, or -1 when it is not there.</summary>
    public int IndexOf(long id) => items.FindIndex(item => item.Id == id);
}
