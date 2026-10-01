namespace Flint.Core.Settings;

/// <summary>The two ways a saved value is brought back inside what its control offers.</summary>
internal static class SettingsRange
{
    /// <summary>
    /// The offered value nearest <paramref name="value"/>, the smaller on a tie, so a number nudged
    /// by hand lands where the person was evidently aiming.
    /// </summary>
    internal static int Nearest(IReadOnlyList<int> offered, int value) =>
        offered.OrderBy(candidate => Math.Abs((long)candidate - value)).ThenBy(candidate => candidate).First();

    /// <summary><paramref name="value"/> when it names a member of its enum; otherwise <paramref name="fallback"/>.</summary>
    internal static T Defined<T>(T value, T fallback)
        where T : struct, Enum => Enum.IsDefined(value) ? value : fallback;
}
