using System.Collections;
using System.Collections.Immutable;

namespace Flint.Protocol;

/// <summary>
/// An immutable byte sequence with structural equality.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>BinaryData</c> in REX Cast's Kotlin protocol module, and exists for the same reason:
/// protocol messages are records whose whole point is value equality, and neither
/// <see cref="ImmutableArray{T}"/> nor an array gives that. <see cref="ImmutableArray{T}"/> in
/// particular compares the underlying array <em>reference</em>, so two identical payloads decoded
/// from the same bytes would compare unequal — which silently makes every round-trip assertion
/// vacuous rather than failing loudly.
/// </para>
/// <para>
/// Copies on construction, so a caller mutating its buffer afterwards cannot alter a message that
/// has already been built.
/// </para>
/// </remarks>
public readonly struct BinaryData : IEquatable<BinaryData>, IReadOnlyList<byte>
{
    private readonly byte[]? _bytes;

    private BinaryData(byte[] bytes) => _bytes = bytes;

    /// <summary>The empty sequence.</summary>
    public static BinaryData Empty { get; } = new([]);

    /// <summary>Copies a span into a new sequence.</summary>
    public static BinaryData From(ReadOnlySpan<byte> bytes) =>
        bytes.IsEmpty ? Empty : new BinaryData(bytes.ToArray());

    /// <summary>Copies an enumerable into a new sequence.</summary>
    public static BinaryData From(IEnumerable<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return From(bytes.ToArray().AsSpan());
    }

    /// <summary>Wraps a byte array by copying it.</summary>
    public static implicit operator BinaryData(byte[] bytes) => From(bytes.AsSpan());

    /// <summary>Number of bytes.</summary>
    public int Count => _bytes?.Length ?? 0;

    /// <summary>Number of bytes. Reads better than <see cref="Count"/> at most call sites.</summary>
    public int Length => Count;

    /// <summary>Whether the sequence is empty.</summary>
    public bool IsEmpty => Count == 0;

    /// <summary>The byte at <paramref name="index"/>.</summary>
    public byte this[int index] =>
        _bytes is null ? throw new IndexOutOfRangeException() : _bytes[index];

    /// <summary>A read-only view, without copying.</summary>
    public ReadOnlySpan<byte> Span => _bytes ?? [];

    /// <summary>A defensive copy as an array.</summary>
    public byte[] ToArray() => _bytes is null ? [] : [.. _bytes];

    /// <inheritdoc />
    public bool Equals(BinaryData other) => Span.SequenceEqual(other.Span);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BinaryData other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // A content hash, necessarily: two sequences that compare equal must hash equal.
        var hash = new HashCode();
        hash.AddBytes(Span);
        return hash.ToHashCode();
    }

    /// <summary>Whether two sequences hold the same bytes.</summary>
    public static bool operator ==(BinaryData left, BinaryData right) => left.Equals(right);

    /// <summary>Whether two sequences differ.</summary>
    public static bool operator !=(BinaryData left, BinaryData right) => !left.Equals(right);

    /// <inheritdoc />
    public IEnumerator<byte> GetEnumerator() => ((IEnumerable<byte>)(_bytes ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => $"BinaryData({Count} bytes)";
}
