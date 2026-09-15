using Flint.App.Services;
using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserProfileSessionRestorerTests
{
    [Fact]
    public void RestoreWaitsForMatchingProfileEpochAndFreshSnapshots()
    {
        var remote = new RecordingCockpitRemote();
        using var restore = new BrowserProfileSessionRestorer(remote, _ => { }, _ => { });
        var saved = new BrowserSavedSession([new("https://saved.test/")], 0, [], 0);
        restore.Observe(Tabs(6));
        restore.Observe(Workspace(6));
        restore.Select(Profile(7, BrowserProfileStorageLocation.WindowsDevice), saved);
        restore.Observe(Tabs(6)); restore.Observe(Workspace(6));
        remote.WorkspaceCommands.ShouldBeEmpty();
        restore.CanCapture.ShouldBeFalse();
        restore.Observe(Tabs(7)); restore.Observe(Workspace(7));
        remote.WorkspaceCommands.ShouldHaveSingleItem();
        remote.WorkspaceCommands[0].ShouldBeOfType<SwitchBrowserWorkspaceModeCommand>();
    }

    [Fact]
    public void Retry_IsNoOpUntilARestoreHasFailed()
    {
        var remote = new RecordingCockpitRemote();
        using var restore = new BrowserProfileSessionRestorer(remote, _ => { }, _ => { });
        restore.Retry();
        remote.WorkspaceCommands.ShouldBeEmpty();
        restore.Select(Profile(7, BrowserProfileStorageLocation.WindowsDevice),
            new BrowserSavedSession([new("https://saved.test/")], 0, [], 0));
        restore.Retry();
        remote.WorkspaceCommands.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProfileChangeCancelsRestoreBeforeAnyFurtherCommands()
    {
        var remote = new RecordingCockpitRemote();
        using var restore = new BrowserProfileSessionRestorer(remote, _ => { }, _ => { });
        restore.Select(Profile(7, BrowserProfileStorageLocation.WindowsDevice),
            new BrowserSavedSession([new("https://saved.test/")], 0, [], 0));
        restore.Observe(Tabs(7)); restore.Observe(Workspace(7));
        remote.WorkspaceCommands.Count.ShouldBe(1);
        restore.Select(Profile(7, BrowserProfileStorageLocation.Television), BrowserSavedSession.Empty);
        restore.Observe(Workspace(7) with { IsWorkspaceMode = false });
        await Task.Delay(30);
        remote.TabRequests.ShouldBeEmpty();
        remote.WorkspaceCommands.Count.ShouldBe(1);
    }

    private static BrowserProfilesSnapshot Profile(long epoch, BrowserProfileStorageLocation storage) =>
        new(epoch, 1, storage, storage == BrowserProfileStorageLocation.Television ? "family" : "", "PC", []);
    private static BrowserTabsSnapshot Tabs(long epoch) => new(epoch, 1, 1,
        [new(1, "", "about:blank", 100, false, false, false, false)]);
    private static BrowserWorkspaceSnapshot Workspace(long epoch) => new(epoch, 1,
        new(true, 2, BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            true, true, true, true, true, true), BrowserWorkspaceLayout.Single, null, [],
        CanResize: true, IsWorkspaceMode: true, SupportsCustomization: true);
}
