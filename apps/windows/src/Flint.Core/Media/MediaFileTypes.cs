namespace Flint.Core.Media;

/// <summary>What sort of thing a file holds, as far as the TV's player is concerned.</summary>
public enum MediaKind
{
    /// <summary>Not a kind of file Flint knows how to play.</summary>
    Unknown = 0,

    /// <summary>Moving pictures, usually with sound.</summary>
    Video = 1,

    /// <summary>Sound only.</summary>
    Audio = 2,

    /// <summary>A still picture, shown for a while.</summary>
    Picture = 3,
}

/// <summary>How Flint sends a file.</summary>
/// <param name="MimeType">What the TV's player is told the file is.</param>
/// <param name="Kind">What sort of thing it holds.</param>
public sealed record MediaFileType(string MimeType, MediaKind Kind)
{
    /// <summary>Whether this is a picture, which has no position to seek or play to.</summary>
    public bool IsPicture => Kind is MediaKind.Picture;
}

/// <summary>Which files Flint offers to play, and how it describes each to the TV.</summary>
/// <remarks>
/// <para>
/// Judged by extension, because that is what a person sees and what the file picker filters on.
/// The TV's player looks at the bytes itself, so a mislabelled file still plays if it can.
/// </para>
/// <para>
/// Every kind listed here has been played on a Fire TV Stick 4K (Fire OS 6.7.1.1) through Flint,
/// by the hardware test <c>MediaHardwareTests.EveryFileTypeFlintOffers_PlaysOnTheTv</c>, which
/// fails if one stops playing. GIF is not listed: the TV's player refuses it.
/// </para>
/// </remarks>
public static class MediaFileTypes
{
    /// <summary>What a file of no known kind is sent as.</summary>
    public const string UnknownMimeType = "application/octet-stream";

    /// <summary>What every file of no known kind is: one value, rather than a new one per question.</summary>
    private static readonly MediaFileType Unknown = new(UnknownMimeType, MediaKind.Unknown);

    private static readonly Dictionary<string, MediaFileType> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = new("video/mp4", MediaKind.Video),
        [".m4v"] = new("video/mp4", MediaKind.Video),
        [".mkv"] = new("video/x-matroska", MediaKind.Video),
        [".webm"] = new("video/webm", MediaKind.Video),
        [".mov"] = new("video/quicktime", MediaKind.Video),
        [".ts"] = new("video/mp2t", MediaKind.Video),
        [".mp3"] = new("audio/mpeg", MediaKind.Audio),
        [".m4a"] = new("audio/mp4", MediaKind.Audio),
        [".aac"] = new("audio/aac", MediaKind.Audio),
        [".flac"] = new("audio/flac", MediaKind.Audio),
        [".wav"] = new("audio/wav", MediaKind.Audio),
        [".ogg"] = new("audio/ogg", MediaKind.Audio),
        [".opus"] = new("audio/ogg", MediaKind.Audio),
        [".jpg"] = new("image/jpeg", MediaKind.Picture),
        [".jpeg"] = new("image/jpeg", MediaKind.Picture),
        [".png"] = new("image/png", MediaKind.Picture),
        [".webp"] = new("image/webp", MediaKind.Picture),
        [".bmp"] = new("image/bmp", MediaKind.Picture),
    };

    /// <summary>
    /// The same table, asked with part of a path, so judging a folder of thousands of files copies
    /// none of their extensions.
    /// </summary>
    private static readonly Dictionary<string, MediaFileType>.AlternateLookup<ReadOnlySpan<char>> ByExtension =
        Known.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Every extension Flint offers, with its leading dot, for a file picker's filter.</summary>
    public static IReadOnlyCollection<string> Extensions => Known.Keys;

    /// <summary>How a file at <paramref name="path"/> is sent.</summary>
    public static MediaFileType For(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ByExtension.TryGetValue(Path.GetExtension(path.AsSpan()), out var type) ? type : Unknown;
    }

    /// <summary>Whether a file at <paramref name="path"/> is one Flint offers to play.</summary>
    public static bool IsPlayable(string path) => For(path).Kind is not MediaKind.Unknown;
}
