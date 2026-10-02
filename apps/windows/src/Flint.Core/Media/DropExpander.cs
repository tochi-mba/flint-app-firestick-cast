using Flint.Core.Settings;

namespace Flint.Core.Media;

/// <summary>What a file is on disk, as far as the queue cares.</summary>
/// <param name="SizeBytes">How long it is.</param>
/// <param name="LastWritten">When it was last changed.</param>
public sealed record MediaFileFacts(long SizeBytes, DateTimeOffset LastWritten);

/// <summary>The few questions the queue asks of the disk, behind an interface so they can be tested.</summary>
public interface IMediaFileSystem
{
    /// <summary>Whether <paramref name="path"/> is a folder.</summary>
    bool IsFolder(string path);

    /// <summary>The size and time of the file at <paramref name="path"/>, or null when it is not there.</summary>
    MediaFileFacts? Facts(string path);

    /// <summary>Whether Windows marks <paramref name="path"/> hidden or as part of the system.</summary>
    bool IsHiddenOrSystem(string path);

    /// <summary>The files directly in <paramref name="folder"/>. Throws when the folder cannot be read.</summary>
    IEnumerable<string> FilesIn(string folder);

    /// <summary>The folders directly in <paramref name="folder"/>. Throws when the folder cannot be read.</summary>
    IEnumerable<string> FoldersIn(string folder);
}

/// <summary>What a drop added up to.</summary>
/// <param name="Files">The playable files, in the order they will be queued.</param>
/// <param name="Capped">Whether more were found than are queued at once.</param>
/// <param name="Skipped">Folders that could not be read.</param>
public sealed record DropContents(IReadOnlyList<string> Files, bool Capped, int Skipped);

/// <summary>
/// Turns what was dropped on Flint, or chosen in its file picker, into the files to queue.
/// </summary>
/// <remarks>
/// Folders are opened, their own subfolders only when the person asked for that. Hidden and system
/// files are left out, and so are shortcuts, which Flint does not follow. A folder that cannot be
/// read is counted and passed over rather than ending the drop. A folder's files are always listed
/// by name; the dropped items themselves are by name or in the order dropped, as the person chose.
/// </remarks>
public static class DropExpander
{
    /// <summary>The most files one drop queues.</summary>
    public const int MaximumFiles = 2_000;

    /// <summary>Expands <paramref name="dropped"/> into playable files.</summary>
    public static DropContents Expand(
        IReadOnlyList<string> dropped,
        IMediaFileSystem files,
        bool includeSubfolders,
        DropOrder order)
    {
        ArgumentNullException.ThrowIfNull(dropped);
        ArgumentNullException.ThrowIfNull(files);
        var ordered = order is DropOrder.ByName
            ? [.. dropped.Order(NaturalOrderByName.Instance)]
            : dropped;

        var found = new List<string>();
        var skipped = 0;
        var capped = false;
        foreach (var path in ordered)
        {
            if (capped)
            {
                break;
            }

            if (files.IsFolder(path))
            {
                skipped += Walk(path, files, includeSubfolders, found, ref capped);
            }
            else if (IsWanted(path, files))
            {
                capped = !TryAdd(found, path);
            }
        }

        return new DropContents(found, capped, skipped);
    }

    private static int Walk(string folder, IMediaFileSystem files, bool deep, List<string> found, ref bool capped)
    {
        string[] here;
        string[] below;
        try
        {
            here = [.. files.FilesIn(folder).Order(NaturalOrderByName.Instance)];
            below = deep ? [.. files.FoldersIn(folder).Order(NaturalOrderByName.Instance)] : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 1;
        }

        foreach (var path in here)
        {
            if (IsWanted(path, files) && !TryAdd(found, path))
            {
                capped = true;
                return 0;
            }
        }

        var skipped = 0;
        foreach (var child in below)
        {
            if (!files.IsHiddenOrSystem(child))
            {
                skipped += Walk(child, files, deep, found, ref capped);
                if (capped)
                {
                    break;
                }
            }
        }

        return skipped;
    }

    private static bool IsWanted(string path, IMediaFileSystem files) =>
        MediaFileTypes.IsPlayable(path) && !files.IsHiddenOrSystem(path);

    private static bool TryAdd(List<string> found, string path)
    {
        if (found.Count >= MaximumFiles)
        {
            return false;
        }

        found.Add(path);
        return true;
    }

    /// <summary>Orders paths by their last part, the way a folder window lists them.</summary>
    private sealed class NaturalOrderByName : IComparer<string>
    {
        public static NaturalOrderByName Instance { get; } = new();

        public int Compare(string? x, string? y) =>
            NaturalOrder.Instance.Compare(Path.GetFileName(x), Path.GetFileName(y)) is var byName and not 0
                ? byName
                : NaturalOrder.Instance.Compare(x, y);
    }
}
