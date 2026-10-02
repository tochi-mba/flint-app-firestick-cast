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

/// <summary>How Flint sends a file, and whether it has been tried on a TV.</summary>
/// <param name="MimeType">What the TV's player is told the file is.</param>
/// <param name="Kind">What sort of thing it holds.</param>
/// <param name="Tried">
/// Whether this kind of file has been seen to play on a Fire TV. A file that has not is still sent,
/// and the queue says so rather than promising it will play.
/// </param>
public sealed record MediaFileType(string MimeType, MediaKind Kind, bool Tried)
{
    /// <summary>Whether this is a picture, which has no position to seek or play to.</summary>
    public bool IsPicture => Kind is MediaKind.Picture;
}

/// <summary>Which files Flint offers to play, and how it describes each to the TV.</summary>
/// <remarks>
/// Judged by extension, because that is what a person sees and what the file picker filters on.
/// The TV's player looks at the bytes itself, so a mislabelled file still plays if it can.
/// </remarks>
public static class MediaFileTypes
{
    /// <summary>What a file of no known kind is sent as.</summary>
    public const string UnknownMimeType = "application/octet-stream";

    private static readonly Dictionary<string, MediaFileType> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // Played on a Fire TV through Flint before this list existed.
        [".mp4"] = new("video/mp4", MediaKind.Video, Tried: true),
        [".mkv"] = new("video/x-matroska", MediaKind.Video, Tried: true),
        [".webm"] = new("video/webm", MediaKind.Video, Tried: true),
        [".mp3"] = new("audio/mpeg", MediaKind.Audio, Tried: true),
        [".m4a"] = new("audio/mp4", MediaKind.Audio, Tried: true),
        [".jpg"] = new("image/jpeg", MediaKind.Picture, Tried: true),
        [".jpeg"] = new("image/jpeg", MediaKind.Picture, Tried: true),
        [".png"] = new("image/png", MediaKind.Picture, Tried: true),

        // Formats the TV's player supports, not yet seen to play through Flint.
        [".m4v"] = new("video/mp4", MediaKind.Video, Tried: false),
        [".mov"] = new("video/quicktime", MediaKind.Video, Tried: false),
        [".ts"] = new("video/mp2t", MediaKind.Video, Tried: false),
        [".aac"] = new("audio/aac", MediaKind.Audio, Tried: false),
        [".flac"] = new("audio/flac", MediaKind.Audio, Tried: false),
        [".wav"] = new("audio/wav", MediaKind.Audio, Tried: false),
        [".ogg"] = new("audio/ogg", MediaKind.Audio, Tried: false),
        [".opus"] = new("audio/ogg", MediaKind.Audio, Tried: false),
        [".webp"] = new("image/webp", MediaKind.Picture, Tried: false),
        [".bmp"] = new("image/bmp", MediaKind.Picture, Tried: false),
        [".gif"] = new("image/gif", MediaKind.Picture, Tried: false),
    };

    /// <summary>Every extension Flint offers, with its leading dot, for a file picker's filter.</summary>
    public static IReadOnlyCollection<string> Extensions => Known.Keys;

    /// <summary>How a file at <paramref name="path"/> is sent.</summary>
    public static MediaFileType For(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Known.TryGetValue(Path.GetExtension(path), out var type)
            ? type
            : new MediaFileType(UnknownMimeType, MediaKind.Unknown, Tried: false);
    }

    /// <summary>Whether a file at <paramref name="path"/> is one Flint offers to play.</summary>
    public static bool IsPlayable(string path) => For(path).Kind is not MediaKind.Unknown;
}
