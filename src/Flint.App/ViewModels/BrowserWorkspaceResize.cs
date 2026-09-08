using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

public sealed partial class BrowserWorkspaceViewModel
{
    private bool resizeAvailable;
    private ResizeBrowserWorkspaceCommand? queuedResize;
    private bool sendingResize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResize))]
    [NotifyPropertyChangedFor(nameof(CanSwitchMode))]
    [NotifyPropertyChangedFor(nameof(CanAddPane))]
    [NotifyPropertyChangedFor(nameof(CanSendConfirmedInput))]
    private bool isRestoringProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendConfirmedInput))]
    private bool isWorkspaceMode = true;
    [ObservableProperty] private bool supportsCustomization;

    public bool CanSwitchMode => !IsRestoringProfile && IsAvailable && commandSink is not null && SupportsCustomization;

    [RelayCommand]
    private Task ShowTabsAsync() => SwitchModeAsync(false);
    [RelayCommand]
    private Task ShowWorkspaceAsync() => SwitchModeAsync(true);

    public Task<bool> SwitchModeAsync(bool workspace) => CanSwitchMode
        ? SendSafelyAsync(new SwitchBrowserWorkspaceModeCommand(workspace, ColumnSplit, RowSplit))
        : Task.FromResult(false);

    [ObservableProperty] private int columnSplit = 5000;
    [ObservableProperty] private int rowSplit = 5000;

    /// <summary>Only a receiver that published geometry can receive resize commands.</summary>
    public bool CanResize => !IsRestoringProfile && IsAvailable && commandSink is not null && resizeAvailable && Panes.Count > 1;

    private void ApplyGeometry(BrowserWorkspaceSnapshot snapshot)
    {
        resizeAvailable = snapshot.CanResize;
        IsWorkspaceMode = snapshot.IsWorkspaceMode;
        SupportsCustomization = snapshot.SupportsCustomization;
        OnPropertyChanged(nameof(CanSwitchMode));
        ColumnSplit = Math.Clamp(snapshot.ColumnSplit, 1500, 8500);
        RowSplit = Math.Clamp(snapshot.RowSplit, 1500, 8500);
        OnPropertyChanged(nameof(CanResize));
    }

    /// <summary>Coalesces rapid drag updates; unsent sizes belong to their original connection.</summary>
    public async Task ResizeAsync(int column, int row)
    {
        if (!CanResize) return;
        queuedResize = new(Math.Clamp(column, 1500, 8500), Math.Clamp(row, 1500, 8500));
        if (sendingResize) return;
        sendingResize = true;
        var generation = connectionGeneration;
        try
        {
            while (queuedResize is { } next && generation == connectionGeneration && CanResize)
            {
                queuedResize = null;
                if (!await SendSafelyAsync(next)) break;
                await Task.Delay(50);
            }
        }
        finally
        {
            queuedResize = null;
            sendingResize = false;
        }
    }

    /// <summary>Copies a tab URL into a new page; the original tab remains available.</summary>
    public async Task<bool> AddTabAsync(string url)
    {
        if (!CanAddPane) { CommandError = "This TV has no room for another pane. Replace a selected pane instead."; return false; }
        if (!IsWorkspaceMode && !await SwitchModeAsync(true)) return false;
        return await SendSafelyAsync(new CreateBrowserWorkspacePaneCommand(url));
    }

    /// <summary>Replaces only the explicitly selected pane's page.</summary>
    public async Task<bool> ReplacePaneFromTabAsync(string url)
    {
        if (confirmedFocusedPaneId is not { } pane || pendingFocusPaneId is not null) return false;
        if (!IsWorkspaceMode && !await SwitchModeAsync(true)) return false;
        return await SendSafelyAsync(new BrowserWorkspacePageCommand(pane, BrowserWorkspacePageAction.Navigate, url));
    }

    [RelayCommand]
    private Task EqualizePanesAsync() => ResizeAsync(5000, 5000);
}
