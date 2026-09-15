using Flint.App.Services;

namespace Flint.App.ViewModels;

/// <summary>Restores saved URLs only after receiver snapshots confirm each structural change.</summary>
internal sealed class BrowserProfileSessionRestorer(IBrowserCockpitRemote remote,
    Action<string?> reportError, Action<bool> reportBusy) : IDisposable
{
    private CancellationTokenSource cancellation = new();
    private TaskCompletionSource signal = NewSignal();
    private BrowserTabsSnapshot? tabs;
    private BrowserWorkspaceSnapshot? workspace;
    private BrowserSavedSession? pending;
    private string? selection;
    private bool running;
    private bool failed;
    private long selectedEpoch;
    public bool CanCapture => selection is not null && pending is null && !running;

    public void Select(BrowserProfilesSnapshot profile, BrowserSavedSession saved, Guid deviceProfile = default)
    {
        var key = $"{profile.Epoch}:{profile.ActiveStorage}:{profile.ActiveProfileId}:{deviceProfile}";
        if (key == selection) return;
        cancellation.Cancel(); cancellation.Dispose(); cancellation = new();
        selection = key;
        selectedEpoch = profile.Epoch;
        // A same-epoch profile switch must wait for fresh snapshots owned by the new profile.
        tabs = null; workspace = null;
        running = false; failed = false;
        pending = profile.ActiveStorage == BrowserProfileStorageLocation.WindowsDevice &&
            (saved.Tabs.Length > 0 || saved.Panes.Length > 0) ? saved.Normalize() : null;
        reportBusy(pending is not null);
        TryStart();
    }

    public void Observe(BrowserTabsSnapshot value) { tabs = value; Pulse(); TryStart(); }
    public void Observe(BrowserWorkspaceSnapshot value) { workspace = value; Pulse(); TryStart(); }
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private void Pulse() { var old = signal; signal = NewSignal(); old.TrySetResult(); }

    private void TryStart()
    {
        if (running || failed || pending is null || tabs is null || workspace?.SupportsCustomization != true ||
            tabs.Epoch != selectedEpoch || workspace.Epoch != selectedEpoch || remote.WorkspaceCommandSink is null) return;
        running = true;
        _ = RestoreAsync(pending, cancellation.Token);
    }

    private async Task RestoreAsync(BrowserSavedSession saved, CancellationToken token)
    {
        try
        {
            reportError(null);
            await Mode(false, saved, token);
            if (saved.Tabs.Length > 0)
            {
                // Keep one placeholder until a replacement exists; never close the last TV tab.
                foreach (var tab in tabs!.Tabs.Skip(1).ToArray())
                    await Tab(new(BrowserTabOperation.Close, tab.Id), () => tabs!.Tabs.All(t => t.Id != tab.Id), token);
                var placeholder = tabs!.Tabs.FirstOrDefault()?.Id;
                foreach (var page in saved.Tabs)
                {
                    var count = tabs!.Tabs.Count;
                    await Tab(new(BrowserTabOperation.New, Url: page.Url), () => tabs!.Tabs.Count > count, token);
                    if (placeholder is { } id)
                    {
                        await Tab(new(BrowserTabOperation.Close, id), () => tabs!.Tabs.All(t => t.Id != id), token);
                        placeholder = null;
                    }
                }
                var active = tabs!.Tabs[Math.Min(saved.ActiveTab, tabs.Tabs.Count - 1)].Id;
                await Tab(new(BrowserTabOperation.Select, active), () => tabs!.ActiveTabId == active, token);
            }
            if (saved.Panes.Length > 0)
            {
                await Mode(true, saved, token);
                var maximum = workspace!.Capabilities.MaximumVisiblePanes;
                if (saved.Panes.Length > maximum)
                    throw new InvalidOperationException("This TV cannot restore all saved panes. Your saved layout has been kept.");
                while (workspace!.Panes.Count > saved.Panes.Length)
                {
                    var id = workspace.Panes[^1].PaneId;
                    await Workspace(new CloseBrowserWorkspacePaneCommand(id), () => workspace!.Panes.All(p => p.PaneId != id), token);
                }
                for (var i = 0; i < saved.Panes.Length; i++)
                {
                    var index = i;
                    var page = saved.Panes[i];
                    if (i >= workspace!.Panes.Count)
                        await Workspace(new CreateBrowserWorkspacePaneCommand(page.Url), () => workspace!.Panes.Count > index, token);
                    var pane = workspace!.Panes.OrderBy(p => p.Slot).ElementAt(i);
                    await Workspace(new BrowserWorkspacePageCommand(pane.PaneId, BrowserWorkspacePageAction.Navigate, page.Url),
                        () => workspace!.Panes.Any(p => p.PaneId == pane.PaneId && p.Url == page.Url), token);
                    if (page.Muted)
                        await remote.WorkspaceCommandSink!.SendAsync(new RequestBrowserWorkspaceMediaCommand(pane.PaneId, BrowserWorkspaceMediaActions.Mute), token);
                }
                var layout = Enum.Parse<BrowserWorkspaceLayout>(saved.Layout);
                await Workspace(new SetBrowserWorkspaceLayoutCommand(layout), () => workspace!.Layout == layout, token);
                await Workspace(new ResizeBrowserWorkspaceCommand(saved.Column, saved.Row),
                    () => workspace!.ColumnSplit == saved.Column && workspace.RowSplit == saved.Row, token);
                var focused = workspace!.Panes.OrderBy(p => p.Slot).ElementAt(saved.ActivePane).PaneId;
                await Workspace(new FocusBrowserWorkspacePaneCommand(focused), () => workspace!.FocusedPaneId == focused, token);
            }
            await Mode(saved.WorkspaceMode, saved, token);
            token.ThrowIfCancellationRequested();
            pending = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                failed = true;
                reportError(error is InvalidOperationException ? error.Message :
                    "Your saved pages were kept, but restore did not finish. Reconnect to try again.");
            }
        }
        finally
        {
            if (!token.IsCancellationRequested) { running = false; reportBusy(false); }
        }
    }

    public void Retry()
    {
        if (!failed || pending is null) return;
        failed = false;
        reportBusy(true);
        TryStart();
    }

    private Task Mode(bool mode, BrowserSavedSession saved, CancellationToken token) =>
        Workspace(new SwitchBrowserWorkspaceModeCommand(mode, saved.Column, saved.Row), () => workspace!.IsWorkspaceMode == mode, token);

    private async Task Tab(BrowserTabRequest command, Func<bool> confirmed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await remote.SendTabCommandAsync(command, token);
        await Wait(confirmed, token);
    }

    private async Task Workspace(BrowserWorkspaceCommand command, Func<bool> confirmed, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await remote.WorkspaceCommandSink!.SendAsync(command, token);
        await Wait(confirmed, token);
    }

    private async Task Wait(Func<bool> confirmed, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (!confirmed()) await signal.Task.WaitAsync(timeout.Token);
        token.ThrowIfCancellationRequested();
    }

    public void Dispose() { cancellation.Cancel(); cancellation.Dispose(); reportBusy(false); }
}
