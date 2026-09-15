using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Flint.App.ViewModels;

namespace Flint.App.Views;

public partial class MediaPage : UserControl
{
    public MediaPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private CastPageViewModel? viewModel;

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (viewModel is not null)
        {
            viewModel.MediaFileSelectionRequested -= SelectMediaFileAsync;
        }

        viewModel = DataContext as CastPageViewModel;
        if (viewModel is not null)
        {
            viewModel.MediaFileSelectionRequested += SelectMediaFileAsync;
        }
    }

    private async void SelectMediaFileAsync(object? sender, EventArgs eventArgs)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null || viewModel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose media to play",
            AllowMultiple = false,
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            await viewModel.LoadMediaFileCommand.ExecuteAsync(path);
        }
    }
}
