using Avalonia.Controls;
using Avalonia.Input.Platform;
using Flint.App.ViewModels.Settings;

namespace Flint.App.Views;

/// <summary>The About section. Only the clipboard lives here; what happens around it is the view model's.</summary>
public partial class AboutSettingsSection : UserControl
{
    private AboutSettingsViewModel? viewModel;

    /// <summary>Creates the section.</summary>
    public AboutSettingsSection()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (viewModel is not null)
        {
            viewModel.CopyRequested -= OnCopyRequested;
        }

        viewModel = DataContext as AboutSettingsViewModel;
        if (viewModel is not null)
        {
            viewModel.CopyRequested += OnCopyRequested;
        }
    }

    private async void OnCopyRequested(object? sender, EventArgs args)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        await ((AboutSettingsViewModel)sender!).CopyReportWithAsync(clipboard is null ? null : clipboard.SetTextAsync);
    }
}
