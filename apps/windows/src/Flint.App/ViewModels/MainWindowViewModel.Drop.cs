using CommunityToolkit.Mvvm.ComponentModel;

namespace Flint.App.ViewModels;

/// <summary>Files and folders dropped anywhere on the window.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>Whether files are being dragged over the window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DropText))]
    private bool _isDropTarget;

    /// <summary>What dropping now would do.</summary>
    public string DropText =>
        Media.NowPlaying.IsActive ? "Drop to add to the queue"
        : Cast.IsSessionConnected ? $"Drop to play on {Cast.TvInSentence}"
        : "Drop to add to the queue. Connect to your TV to play them.";

    /// <summary>Queues what was dropped, on the Media page, where the queue is.</summary>
    public async Task DropFilesAsync(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        IsDropTarget = false;
        if (paths.Count == 0)
        {
            return;
        }

        Selected = Destinations.Single(destination => destination.Label == "Media");
        await Media.Queue.AddDroppedAsync(paths).ConfigureAwait(true);
    }
}
