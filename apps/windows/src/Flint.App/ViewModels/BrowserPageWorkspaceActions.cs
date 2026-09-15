using CommunityToolkit.Mvvm.Input;

namespace Flint.App.ViewModels;

public sealed partial class BrowserPageViewModel
{
    private async Task<bool> BeforeProfileTabCommandAsync(Flint.App.Services.BrowserTabRequest request,
        CancellationToken cancellationToken)
    {
        if (Workspace.IsRestoringProfile) return false;
        if (Workspace.SupportsCustomization && Workspace.IsWorkspaceMode &&
            !await Workspace.SwitchModeAsync(false)) return false;
        return await tabSession.BeforeTabCommandAsync(request, cancellationToken);
    }

    [RelayCommand]
    private async Task AddSelectedTabToWorkspaceAsync()
    {
        var tab = Tabs.Items.FirstOrDefault(t => t.Id == Tabs.ActiveId);
        if (tab is not null && !string.IsNullOrWhiteSpace(tab.Url)) await Workspace.AddTabAsync(tab.Url);
    }

    [RelayCommand]
    private async Task ReplaceSelectedPaneFromTabAsync()
    {
        var tab = Tabs.Items.FirstOrDefault(t => t.Id == Tabs.ActiveId);
        if (tab is not null && !string.IsNullOrWhiteSpace(tab.Url)) await Workspace.ReplacePaneFromTabAsync(tab.Url);
    }
}
