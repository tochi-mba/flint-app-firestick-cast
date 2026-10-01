using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Flint.App.Services;
using Flint.App.ViewModels.Settings;

namespace Flint.App.Views;

/// <summary>The Privacy and data section. Only the file pickers live here; what follows them is tested elsewhere.</summary>
public partial class PrivacySettingsSection : UserControl
{
    private PrivacySettingsViewModel? viewModel;

    /// <summary>Creates the section.</summary>
    public PrivacySettingsSection()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (viewModel is not null)
        {
            viewModel.ExportRequested -= OnExportRequested;
            viewModel.ImportRequested -= OnImportRequested;
        }

        viewModel = DataContext as PrivacySettingsViewModel;
        if (viewModel is not null)
        {
            viewModel.ExportRequested += OnExportRequested;
            viewModel.ImportRequested += OnImportRequested;
        }
    }

    private async void OnExportRequested(object? sender, EventArgs args) =>
        await SettingsFileTransfer.ExportAsync(PickExportFileAsync, (PrivacySettingsViewModel)sender!);

    private async void OnImportRequested(object? sender, EventArgs args) =>
        await SettingsFileTransfer.ImportAsync(PickImportFileAsync, (PrivacySettingsViewModel)sender!);

    private async Task<Stream?> PickExportFileAsync()
    {
        if (Storage is not { CanSave: true } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Flint settings",
            SuggestedFileName = "flint-settings.json",
            DefaultExtension = "json",
            FileTypeChoices = [SettingsFileTransfer.JsonFiles],
        });
        return file is null ? null : await file.OpenWriteAsync();
    }

    private async Task<Stream?> PickImportFileAsync()
    {
        if (Storage is not { CanOpen: true } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Flint settings",
            AllowMultiple = false,
            FileTypeFilter = [SettingsFileTransfer.JsonFiles],
        });
        return files.Count == 1 ? await files[0].OpenReadAsync() : null;
    }
}
