namespace Flint.Core;

/// <summary>A TV Flint has paired with, and how to reach it again without a code.</summary>
/// <param name="Name">The name the TV gives in its greeting. A token is only ever sent to a TV giving this name.</param>
/// <param name="Address">Where it was last reached. Updated when it is found somewhere else.</param>
/// <param name="ReceiverPort">The receiver's port.</param>
/// <param name="Token">
/// The login the TV granted, or null when it has none: it never gave one, or it stopped accepting it.
/// </param>
/// <param name="LastConnected">When it was last connected to.</param>
public sealed record KnownTv(string Name, string Address, int ReceiverPort, string? Token, DateTimeOffset LastConnected)
{
    /// <summary>Whether Flint can reach this TV without asking for a code.</summary>
    public bool HasLogin => Token is not null;

    /// <inheritdoc />
    /// <remarks>Never the token: a record's own text would put it in any log that printed one.</remarks>
    public override string ToString() => $"{Name} at {Address}:{ReceiverPort}";
}

/// <summary>Where known TVs are kept between runs.</summary>
public interface IKnownTvStore
{
    /// <summary>Every known TV, most recently connected first.</summary>
    IReadOnlyList<KnownTv> Load();

    /// <summary>Remembers <paramref name="tv"/>, replacing what was kept under its name.</summary>
    void Save(KnownTv tv);

    /// <summary>Forgets the TV called <paramref name="name"/>, its login and its address.</summary>
    void Forget(string name);

    /// <summary>Forgets every TV.</summary>
    void ForgetAll();
}

/// <summary>Known TVs kept only as long as the process, for tests and design-time shells.</summary>
public sealed class InMemoryKnownTvStore : IKnownTvStore
{
    private readonly List<KnownTv> tvs = [];

    /// <inheritdoc />
    public IReadOnlyList<KnownTv> Load() => KnownTvList.Ordered(tvs);

    /// <inheritdoc />
    public void Save(KnownTv tv)
    {
        ArgumentNullException.ThrowIfNull(tv);
        var kept = KnownTvList.With(tvs, tv);
        tvs.Clear();
        tvs.AddRange(kept);
    }

    /// <inheritdoc />
    public void Forget(string name) => tvs.RemoveAll(tv => tv.Name == name);

    /// <inheritdoc />
    public void ForgetAll() => tvs.Clear();
}

/// <summary>The rules every known-TV store keeps: one entry a name, newest first, at most eight.</summary>
public static class KnownTvList
{
    /// <summary>The most TVs remembered, matching the remembered-address list.</summary>
    public const int MaxEntries = 8;

    /// <summary><paramref name="tvs"/> newest first.</summary>
    public static IReadOnlyList<KnownTv> Ordered(IEnumerable<KnownTv> tvs) =>
        [.. tvs.OrderByDescending(tv => tv.LastConnected)];

    /// <summary><paramref name="tvs"/> with <paramref name="tv"/> in place of any entry of its name, newest first, capped.</summary>
    public static IReadOnlyList<KnownTv> With(IEnumerable<KnownTv> tvs, KnownTv tv) =>
        [.. Ordered(tvs.Where(existing => existing.Name != tv.Name).Append(tv)).Take(MaxEntries)];
}

/// <summary>When to try again after the TV goes away.</summary>
public static class ReconnectSchedule
{
    /// <summary>The waits before each try: 1, 2, 4 and 8 seconds, then every 15.</summary>
    public static TimeSpan DelayBefore(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        return TimeSpan.FromSeconds(attempt switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            4 => 8,
            _ => 15,
        });
    }
}
