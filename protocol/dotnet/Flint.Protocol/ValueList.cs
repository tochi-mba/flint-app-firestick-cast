using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Flint.Protocol;

/// <summary>
/// An immutable list with structural equality.
/// </summary>
/// <remarks>
/// Protocol messages are records, and a record's value equality is only as good as its fields'.
/// The framework's immutable collections all compare by reference, so a decoded message would
/// never equal the message it was decoded from. This is the collection equivalent of
/// <see cref="BinaryData"/>, and exists for the same reason.
/// </remarks>
/// <typeparam name="T">The element type.</typeparam>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Empty and From construct the list, in the shape ImmutableArray<T> uses; a separate non-generic type would split one concept across two names.")]
public readonly struct ValueList<T> : IEquatable<ValueList<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items;

    private ValueList(T[] items) => _items = items;

    /// <summary>The empty list.</summary>
    public static ValueList<T> Empty { get; } = new([]);

    /// <summary>Copies a sequence into a new list.</summary>
    public static ValueList<T> From(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var array = items.ToArray();
        return array.Length == 0 ? Empty : new ValueList<T>(array);
    }

    /// <summary>Copies a span into a new list.</summary>
    public static ValueList<T> From(ReadOnlySpan<T> items) =>
        items.IsEmpty ? Empty : new ValueList<T>(items.ToArray());

    /// <summary>Wraps an array by copying it.</summary>
    public static implicit operator ValueList<T>(T[] items) => From(items.AsSpan());

    /// <inheritdoc />
    public int Count => _items?.Length ?? 0;

    /// <summary>Number of elements.</summary>
    public int Length => Count;

    /// <summary>Whether the list is empty.</summary>
    public bool IsEmpty => Count == 0;

    /// <inheritdoc />
    public T this[int index] => (_items ?? [])[index];

    /// <inheritdoc />
    public bool Equals(ValueList<T> other)
    {
        if (Count != other.Count)
        {
            return false;
        }

        for (var index = 0; index < Count; index++)
        {
            if (!this[index].Equals(other[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in this)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>Whether two lists hold the same elements in the same order.</summary>
    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);

    /// <summary>Whether two lists differ.</summary>
    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
