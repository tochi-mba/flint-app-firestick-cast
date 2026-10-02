using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Flint.App.Views;

/// <summary>What a drag carries, as far as the queue is concerned.</summary>
internal static class DroppedFiles
{
    /// <summary>Whether the drag carries files or folders, rather than text or a link.</summary>
    public static bool HasFiles(IDataTransfer? transfer) => transfer?.Contains(DataFormat.File) == true;

    /// <summary>The local paths of the files and folders in the drag, in the order given.</summary>
    /// <remarks>Items with no local path, such as a file still inside a phone, are left out.</remarks>
    public static IReadOnlyList<string> PathsOf(IDataTransfer? transfer) =>
        transfer?.TryGetFiles() is { } items
            ? [.. items.Select(item => item.TryGetLocalPath()).OfType<string>()]
            : [];
}
