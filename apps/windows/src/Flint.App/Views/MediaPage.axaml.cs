using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Flint.App.ViewModels;

namespace Flint.App.Views;

public partial class MediaPage : UserControl
{
    public MediaPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Tunnelling, so the page's scroll viewer does not take the arrows first; the keys a
        // focused control answers itself are left to it.
        AddHandler(KeyDownEvent, OnPageKeyDown, RoutingStrategies.Tunnel);
    }

    private MediaPageViewModel? viewModel;

    /// <summary>Asks the person for files, and gives their paths; the system picker unless a test sets one.</summary>
    internal Func<TopLevel, Task<IReadOnlyList<string>>> PickFiles { get; set; } = PickWithSystemPickerAsync;

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        // The bar is listened to only while there is a card for it to drive.
        if (viewModel is not null)
        {
            viewModel.Cast.MediaFileSelectionRequested -= SelectMediaFileAsync;
            Seek.ScrubStarted -= OnScrubStarted;
            Seek.SeekCommitted -= OnSeekCommitted;
        }

        viewModel = DataContext as MediaPageViewModel;
        if (viewModel is not null)
        {
            viewModel.Cast.MediaFileSelectionRequested += SelectMediaFileAsync;
            Seek.ScrubStarted += OnScrubStarted;
            Seek.SeekCommitted += OnSeekCommitted;
        }
    }

    /// <summary>A key on a row of the Up next list.</summary>
    private void OnQueueRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel is not null && sender is Control { DataContext: QueueRowViewModel row })
        {
            e.Handled = viewModel.Queue.HandleRowKey(row, e.Key, e.KeyModifiers);
        }
    }

    private void OnScrubStarted(object? sender, double seconds) => viewModel!.NowPlaying.BeginScrub(seconds);

    private void OnSeekCommitted(object? sender, double seconds) => viewModel!.NowPlaying.CommitSeek(seconds);

    /// <summary>
    /// The page's keys, unless someone is typing or the focused control answers them. On the way
    /// down, the key's source is the focused control.
    /// </summary>
    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel is null || e.KeyModifiers != KeyModifiers.None
            || LeftToFocused(e.Source, e.Key))
        {
            return;
        }

        e.Handled = viewModel.NowPlaying.HandleKey(e.Key);
    }

    /// <summary>
    /// Whether the focused control answers <paramref name="key"/> itself: a text box every key, a
    /// slider or the seek bar the arrows, and a button Space.
    /// </summary>
    internal static bool LeftToFocused(object? focused, Key key) => focused switch
    {
        TextBox => true,
        Slider => key is Key.Left or Key.Right or Key.Up or Key.Down,
        Button => key is Key.Space,
        _ => false,
    };

    private async void SelectMediaFileAsync(object? sender, EventArgs eventArgs)
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel || viewModel is null)
        {
            return;
        }

        var paths = await PickFiles(topLevel);
        if (paths.Count > 0)
        {
            await viewModel.Queue.AddDroppedAsync(paths);
        }
    }

    private static async Task<IReadOnlyList<string>> PickWithSystemPickerAsync(TopLevel topLevel)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose media to play",
            AllowMultiple = true,
            FileTypeFilter = [MediaFiles, FilePickerFileTypes.All],
        });
        return PathsOf(files);
    }

    /// <summary>The picker's filter: every kind of file Flint offers.</summary>
    internal static FilePickerFileType MediaFiles { get; } = new("Video, music and pictures")
    {
        Patterns = [.. Flint.Core.Media.MediaFileTypes.Extensions.Order(StringComparer.Ordinal).Select(extension => "*" + extension)],
    };

    /// <summary>The local paths of the files chosen, in the order given; none when nothing was chosen.</summary>
    internal static IReadOnlyList<string> PathsOf(IReadOnlyList<IStorageItem> chosen) =>
        [.. chosen.Select(item => item.TryGetLocalPath()).OfType<string>()];
}
