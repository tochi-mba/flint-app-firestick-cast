namespace Flint.Core.Media;

/// <summary>
/// Orders names the way a person reads them: "Episode 2" before "Episode 10".
/// </summary>
/// <remarks>
/// Runs of digits compare by value, everything else without regard to case. Equal values with
/// different leading zeros fall back to the shorter run first, and names that are otherwise equal
/// fall back to an ordinal comparison, so the order is total and the same on every machine.
/// </remarks>
public sealed class NaturalOrder : IComparer<string>
{
    /// <summary>The one instance; the order holds no state.</summary>
    public static NaturalOrder Instance { get; } = new();

    private NaturalOrder()
    {
    }

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        return y is null ? 1 : Compare(x.AsSpan(), y.AsSpan());
    }

    /// <summary>Compares two names, or two parts of names, without copying either.</summary>
    /// <returns>Less than zero when <paramref name="x"/> comes first, more when it comes second.</returns>
    public static int Compare(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var i = 0;
        var j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }

                var byValue = CompareDigits(x[startX..i], y[startY..j]);
                if (byValue != 0)
                {
                    return byValue;
                }
            }
            else
            {
                // Names in one folder mostly share their start, so equal letters skip the case folding.
                var byLetter = x[i] == y[j] ? 0 : char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (byLetter != 0)
                {
                    return byLetter;
                }

                i++;
                j++;
            }
        }

        var byLength = (x.Length - i).CompareTo(y.Length - j);
        return byLength != 0 ? byLength : Math.Sign(x.SequenceCompareTo(y));
    }

    /// <summary>Compares two runs of digits by value, however long, then by length.</summary>
    private static int CompareDigits(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var trimmedA = a.TrimStart('0');
        var trimmedB = b.TrimStart('0');
        if (trimmedA.Length != trimmedB.Length)
        {
            return trimmedA.Length.CompareTo(trimmedB.Length);
        }

        var byDigits = trimmedA.SequenceCompareTo(trimmedB);
        return byDigits != 0 ? Math.Sign(byDigits) : a.Length.CompareTo(b.Length);
    }
}
