using System.ComponentModel;
using System.Diagnostics;

namespace Flint.App.Services;

/// <summary>Shows a folder in File Explorer.</summary>
public interface IFolderOpener
{
    /// <summary>Opens <paramref name="path"/>, creating it first when it does not exist yet.</summary>
    /// <returns>False when the folder could not be created or shown.</returns>
    bool Open(string path);
}

/// <summary>Opens folders in the real File Explorer.</summary>
public sealed class ExplorerFolderOpener : IFolderOpener
{
    /// <inheritdoc />
    /// <remarks>
    /// Created first because a fresh install has written nothing yet, and an Explorer window that
    /// opens on an error is a worse answer than an empty folder.
    /// </remarks>
    public bool Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            Directory.CreateDirectory(path);
            using var explorer = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                ArgumentList = { path },
                UseShellExecute = false,
            });
            return explorer is not null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or Win32Exception
            or NotSupportedException
            or ArgumentException)
        {
            return false;
        }
    }
}
