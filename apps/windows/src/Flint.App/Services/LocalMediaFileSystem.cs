using Flint.Core.Media;

namespace Flint.App.Services;

/// <summary>The real disk, for the queue.</summary>
public sealed class LocalMediaFileSystem : IMediaFileSystem
{
    /// <inheritdoc />
    public bool IsFolder(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public MediaFileFacts? Facts(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new MediaFileFacts(file.Length, file.LastWriteTimeUtc) : null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsHiddenOrSystem(string path)
    {
        try
        {
            return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            // Not there or not readable: not something to queue either way.
            return true;
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> FilesIn(string folder) => Directory.EnumerateFiles(folder);

    /// <inheritdoc />
    public IEnumerable<string> FoldersIn(string folder) => Directory.EnumerateDirectories(folder);
}
