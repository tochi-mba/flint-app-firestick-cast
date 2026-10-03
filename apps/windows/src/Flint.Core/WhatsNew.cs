namespace Flint.Core;

/// <summary>Remembers which "What's new" highlights this person has already been shown.</summary>
public interface IWhatsNewState
{
    /// <summary>
    /// The highlights already seen, or null when nothing has ever been recorded: a copy of Flint
    /// from before "What's new" existed, or one that has never finished its introduction.
    /// </summary>
    IReadOnlySet<string>? Seen { get; }

    /// <summary>Adds <paramref name="ids"/> to the highlights seen.</summary>
    void MarkSeen(IEnumerable<string> ids);
}

/// <summary>A "What's new" store that lasts only as long as the process, for tests and design-time shells.</summary>
public sealed class InMemoryWhatsNewState : IWhatsNewState
{
    private HashSet<string>? seen;

    /// <summary>Starts with nothing recorded, or with <paramref name="seen"/> already seen.</summary>
    public InMemoryWhatsNewState(IEnumerable<string>? seen = null) =>
        this.seen = seen is null ? null : [.. seen];

    /// <inheritdoc />
    public IReadOnlySet<string>? Seen => seen;

    /// <inheritdoc />
    public void MarkSeen(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        seen ??= [];
        seen.UnionWith(ids);
    }
}

/// <summary>Which highlights to show after an update, and when to show none.</summary>
/// <remarks>
/// <para>
/// A first launch shows the introduction, not a list of changes to a version the person never
/// used, so nothing is new to them: finishing the introduction marks every current highlight seen.
/// </para>
/// <para>
/// A copy that finished its introduction before "What's new" existed has no record at all. It is an
/// update, so everything in the catalogue is new to it. After that, only highlights added since are.
/// Every rolling build carries the same version number, so highlights are tracked by their own ids
/// rather than by comparing versions.
/// </para>
/// </remarks>
public static class WhatsNewPolicy
{
    /// <summary>The highlights to show, in catalogue order; none when there is nothing new.</summary>
    /// <param name="catalogue">Every highlight's id, oldest first.</param>
    /// <param name="seen">What <see cref="IWhatsNewState.Seen"/> reports.</param>
    /// <param name="introductionDone">Whether the first-run introduction has been completed.</param>
    public static IReadOnlyList<string> Unseen(
        IReadOnlyList<string> catalogue,
        IReadOnlySet<string>? seen,
        bool introductionDone)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        if (!introductionDone)
        {
            return [];
        }

        return seen is null ? catalogue : [.. catalogue.Where(id => !seen.Contains(id))];
    }
}
