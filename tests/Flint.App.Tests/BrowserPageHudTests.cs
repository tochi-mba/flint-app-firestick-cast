using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.ViewModels;
using Flint.App.Views;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Headless UI automation for the Web HUD: pages own the stage, chrome is overlay, mosaic fills
/// when the TV advertises workspace panes.
/// </summary>
public sealed class BrowserPageHudTests
{
    [AvaloniaFact]
    public async Task ReadySession_ShowsHudChromeAndInteractivePreviewStage()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var page = new BrowserPage { DataContext = viewModel };
        var window = new Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            viewModel.ShowNavigation.ShouldBeTrue();
            viewModel.ShowAimStage.ShouldBeTrue();
            viewModel.ShowWorkspaceMosaic.ShouldBeFalse();

            Named(page, "Browser HUD chrome").ShouldNotBeNull();
            Named(page, "Browser stage").ShouldNotBeNull();
            Named(page, "Latest TV preview frame").ShouldNotBeNull();
            Named(page, "Browser remote HUD").ShouldNotBeNull();
            Named(page, "Remote control surface").ShouldNotBeNull();
            Named(page, "TV address").ShouldNotBeNull();
            Named(page, "Open extra controls").ShouldNotBeNull();

            Named(page, "TV touchpad").ShouldBeNull();
            page.FindControl<Border>("TouchPad").ShouldBeNull();
            var stage = page.FindControl<Border>("PreviewSurface");
            stage.ShouldNotBeNull();
            stage!.MinHeight.ShouldBeGreaterThanOrEqualTo(520);
            stage.Classes.Contains("hud-dense").ShouldBeFalse();

            var chrome = Named(page, "Browser HUD chrome").ShouldBeOfType<Border>();
            chrome.Classes.Contains("hud-dense").ShouldBeTrue();
            chrome.Bounds.Height.ShouldBeLessThan(140);

            var remoteHud = Named(page, "Browser remote HUD").ShouldBeOfType<Border>();
            remoteHud.Classes.Contains("hud-dense").ShouldBeTrue();
            remoteHud.Bounds.Height.ShouldBeLessThan(120);

            // Dense chrome must leave most of an 800px window for the stage.
            var stageShare = stage.Bounds.Height / window.Bounds.Height;
            stageShare.ShouldBeGreaterThan(0.55);

            // Stacked InfoCards from the old cockpit must not dominate the verified session.
            page.GetVisualDescendants().OfType<TextBlock>()
                .Count(block => block.Text == "TV WORKSPACE")
                .ShouldBe(0);
            // Asserted by control type, not by label text: the dense HUD legitimately labels its
            // own tab strip "TABS", and a word the new design reuses is the wrong way to detect the
            // old cockpit. Effective visibility, not IsVisible — the bootstrap cards keep their own
            // IsVisible true while their container is hidden.
            page.GetVisualDescendants().OfType<Flint.App.Controls.InfoCard>()
                .Count(card => card.IsEffectivelyVisible)
                .ShouldBe(0);
            Named(page, "Page back").ShouldNotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ReadySession_DenseChromeKeepsEveryPrimaryControlReachable()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var page = new BrowserPage { DataContext = viewModel };
        var window = new Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            string[] mustExist =
            [
                "Back",
                "Forward",
                "Reload the TV page",
                "TV address",
                "Open address on TV",
                "Turn the TV preview on or off",
                "Open extra controls",
                "Web help",
                "Close browser on TV",
                "Browser stage",
                "Remote control surface",
                "Up",
                "Down",
                "Left",
                "Right",
                "Select",
                "Page up",
                "Page down",
                "Move focus forward on the page",
                "Page back",
                "Escape",
                "Text to send to the TV",
                "Send text to TV",
                "Keyboard forwarding",
            ];

            foreach (var name in mustExist)
            {
                Named(page, name).ShouldNotBeNull(name);
            }

            Named(page, "Browser HUD chrome")!.Bounds.Height
                .ShouldBeLessThan(Named(page, "Browser stage")!.Bounds.Height);
            Named(page, "Browser remote HUD")!.Bounds.Height
                .ShouldBeLessThan(Named(page, "Browser stage")!.Bounds.Height / 2);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WorkspaceSnapshot_FillsStageWithMosaicPanes()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var page = new BrowserPage { DataContext = viewModel };
        var window = new Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            remote.Cockpit.ShouldNotBeNull();
            remote.Cockpit!.PublishWorkspace(TwoColumnSnapshot());
            window.UpdateLayout();
            await Task.Delay(50);
            window.UpdateLayout();
            // SyncMosaicGrid posts Loaded/Render work after the mosaic ItemsPanel materializes.
            await Task.Delay(50);
            window.UpdateLayout();

            viewModel.ShowWorkspaceMosaic.ShouldBeTrue();
            viewModel.ShowAimStage.ShouldBeFalse();
            viewModel.Workspace.Panes.Count.ShouldBe(2);
            viewModel.Workspace.MosaicColumnCount.ShouldBe(2);

            Named(page, "TV workspace mosaic").ShouldNotBeNull();
            Named(page, "Latest TV preview frame").ShouldNotBeNull();
            // Mosaic chrome must not replace the preview Image — both panes live in that JPEG.
            page.FindControl<Border>("PreviewSurface")!
                .GetVisualDescendants()
                .OfType<Avalonia.Controls.Image>()
                .Any(image => AutomationProperties.GetName(image) == "Latest TV preview frame")
                .ShouldBeTrue();
            page.GetVisualDescendants().OfType<Border>()
                .Count(border => AutomationProperties.GetName(border)?.StartsWith("Pane ", StringComparison.Ordinal) == true)
                .ShouldBe(2);

            var items = page.FindControl<ItemsControl>("WorkspacePaneGrid");
            items.ShouldNotBeNull();
            Flint.App.Controls.WorkspaceMosaicPanel? grid = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                window.UpdateLayout();
                grid = items!.ItemsPanelRoot as Flint.App.Controls.WorkspaceMosaicPanel;
                if (grid is { Columns: 2, Rows: 1 })
                {
                    break;
                }

                await Task.Delay(25);
            }

            grid.ShouldNotBeNull();
            grid!.Columns.ShouldBe(2);
            grid.Rows.ShouldBe(1);

            Named(page, "Focus this TV workspace pane").ShouldNotBeNull();
            Named(page, "Focused workspace pane address").ShouldNotBeNull();
            Named(page, "Open another independently browsing pane on the TV").ShouldNotBeNull();
            Named(page, "Enter page input mode").ShouldNotBeNull();
            Named(page, "Enter mosaic chrome mode").ShouldNotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MorePanel_RevealsSecondaryControlsWithoutLeavingHud()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var page = new BrowserPage { DataContext = viewModel };
        var window = new Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();

            viewModel.IsMorePanelOpen.ShouldBeFalse();
            Named(page, "Clear the TV's browsing data").ShouldBeNull();

            viewModel.ToggleMorePanelCommand.Execute(null);
            window.UpdateLayout();

            viewModel.IsMorePanelOpen.ShouldBeTrue();
            Named(page, "Extra controls scrim").ShouldNotBeNull();
            Named(page, "Clear the TV's browsing data").ShouldNotBeNull();
            Named(page, "Find text").ShouldNotBeNull();
            Named(page, "Close extra controls").ShouldNotBeNull();

            // Stage chrome remains reachable under the HUD model.
            Named(page, "Browser HUD chrome").ShouldNotBeNull();
            Named(page, "TV address").ShouldNotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MosaicLayoutChange_UpdatesUniformGridShape()
    {
        var remote = new RecordingBrowserRemote();
        using var viewModel = await BrowserFixtures.ReadyViewModelAsync(remote);
        var page = new BrowserPage { DataContext = viewModel };
        var window = new Window { Width = 1280, Height = 800, Content = page };
        try
        {
            window.Show();
            remote.Cockpit!.PublishWorkspace(TwoColumnSnapshot());
            window.UpdateLayout();

            remote.Cockpit.PublishWorkspace(FourGridSnapshot());
            window.UpdateLayout();
            await Task.Delay(50);
            window.UpdateLayout();

            viewModel.Workspace.Layout.ShouldBe(BrowserWorkspaceLayout.FourGrid);
            viewModel.Workspace.MosaicColumnCount.ShouldBe(2);
            viewModel.Workspace.MosaicRowCount.ShouldBe(2);

            var items = page.FindControl<ItemsControl>("WorkspacePaneGrid");
            items.ShouldNotBeNull();
            var grid = items!.ItemsPanelRoot as Flint.App.Controls.WorkspaceMosaicPanel;
            grid.ShouldNotBeNull();
            grid!.Columns.ShouldBe(2);
            grid.Rows.ShouldBe(2);
        }
        finally
        {
            window.Close();
        }
    }

    private static Control? Named(Control root, string name) =>
        root.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => AutomationProperties.GetName(control) == name);

    private static BrowserWorkspaceSnapshot TwoColumnSnapshot() =>
        new(
            Epoch: 1,
            Revision: 1,
            Capabilities: Caps(4),
            Layout: BrowserWorkspaceLayout.TwoColumns,
            FocusedPaneId: "one",
            Panes:
            [
                Pane("one", 0, "Example One"),
                Pane("two", 1, "Example Two"),
            ]);

    private static BrowserWorkspaceSnapshot FourGridSnapshot() =>
        new(
            Epoch: 1,
            Revision: 2,
            Capabilities: Caps(4),
            Layout: BrowserWorkspaceLayout.FourGrid,
            FocusedPaneId: "one",
            Panes:
            [
                Pane("one", 0, "One"),
                Pane("two", 1, "Two"),
                Pane("three", 2, "Three"),
                Pane("four", 3, "Four"),
            ]);

    private static BrowserWorkspaceCapabilities Caps(int max) =>
        new(
            IsAvailable: true,
            MaximumVisiblePanes: max,
            SupportedLayouts: BrowserWorkspaceLayoutSet.Single
                | BrowserWorkspaceLayoutSet.TwoColumns
                | BrowserWorkspaceLayoutSet.TwoRows
                | BrowserWorkspaceLayoutSet.FourGrid,
            CanCreatePane: true,
            CanClosePane: true,
            CanRequestPaneFocus: true,
            CanSendFocusedPaneInput: true,
            CanRequestMediaControl: true,
            CanRequestTheaterMode: false);

    private static BrowserWorkspacePaneSnapshot Pane(string id, int slot, string title) =>
        new(
            PaneId: id,
            Slot: slot,
            Url: $"https://{id}.example.test/",
            Title: title,
            Progress: 100,
            State: BrowserWorkspacePaneState.Live,
            Media: BrowserWorkspaceMediaSnapshot.None);
}
