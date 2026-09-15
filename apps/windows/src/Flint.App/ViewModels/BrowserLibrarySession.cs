using Flint.App.Services;

namespace Flint.App.ViewModels;

public sealed partial class BrowserLibraryViewModel
{
    internal BrowserSavedSession SavedDeviceSession => deviceStore.Session(deviceProfile);

    internal void CaptureTabs(BrowserTabsSnapshot snapshot)
    {
        if (PendingProfileName is not null || !IsUsingWindowsDevice || snapshot.Epoch != activeEpoch || snapshot.Tabs.Count == 0) return;
        var saved = deviceStore.Session(deviceProfile);
        var tabs = snapshot.Tabs.ToArray();
        SaveSession(saved with
        {
            Tabs = tabs.Select(t => new BrowserSavedPage(t.Url, t.Title)).ToArray(),
            ActiveTab = Math.Max(0, Array.FindIndex(tabs, t => t.Id == snapshot.ActiveTabId)),
        });
    }

    internal void CaptureWorkspace(BrowserWorkspaceSnapshot snapshot)
    {
        if (PendingProfileName is not null || !IsUsingWindowsDevice || snapshot.Epoch != activeEpoch || snapshot.Panes.Count == 0) return;
        var saved = deviceStore.Session(deviceProfile);
        var panes = snapshot.Panes.OrderBy(p => p.Slot).ToArray();
        SaveSession(saved with
        {
            Panes = panes.Select(p => new BrowserSavedPage(p.Url, p.Title, p.Media.Mute == BrowserWorkspaceMuteState.Muted)).ToArray(),
            ActivePane = Math.Max(0, Array.FindIndex(panes, p => p.PaneId == snapshot.FocusedPaneId)),
            Layout = snapshot.Layout.ToString(), Column = snapshot.ColumnSplit, Row = snapshot.RowSplit, WorkspaceMode = snapshot.IsWorkspaceMode,
        });
    }

    private void SaveSession(BrowserSavedSession saved)
    {
        if (!deviceStore.SaveSession(deviceProfile, saved)) SyncError = "Your browser layout could not be saved on this Windows device.";
    }
}
