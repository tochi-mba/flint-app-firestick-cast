using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Protocol;
using Shouldly;

namespace Flint.App.Tests;

public sealed class BrowserCockpitWireMapperTests
{
    [Fact]
    public void ProfileState_PreservesNamedTvProfilesAndDeviceOwnership()
    {
        var mapped = BrowserCockpitWireMapper.ToProfiles(new BrowserProfileStateMessage(
            9,
            4,
            BrowserProfileSource.Device,
            string.Empty,
            "Office PC",
            ValueList<BrowserProfileEntry>.From(
            [
                new BrowserProfileEntry("family", "Family"),
                new BrowserProfileEntry("guests", "Guests"),
            ])));

        mapped.ActiveStorage.ShouldBe(BrowserProfileStorageLocation.WindowsDevice);
        mapped.DeviceName.ShouldBe("Office PC");
        mapped.TvProfiles.ShouldBe(
        [
            new BrowserTvProfile("family", "Family"),
            new BrowserTvProfile("guests", "Guests"),
        ]);
    }

    [Theory]
    [InlineData(BrowserProfileOperation.SelectTvProfile, BrowserProfileAction.SelectTvProfile)]
    [InlineData(BrowserProfileOperation.CreateTvProfile, BrowserProfileAction.CreateTvProfile)]
    [InlineData(BrowserProfileOperation.RenameTvProfile, BrowserProfileAction.RenameTvProfile)]
    [InlineData(BrowserProfileOperation.DeleteTvProfile, BrowserProfileAction.DeleteTvProfile)]
    [InlineData(BrowserProfileOperation.SelectWindowsDevice, BrowserProfileAction.SelectDevice)]
    [InlineData(BrowserProfileOperation.RequestSnapshot, BrowserProfileAction.RequestSnapshot)]
    public void ProfileRequest_MapsEveryOperationWithoutLosingIdentity(
        BrowserProfileOperation operation,
        BrowserProfileAction expected)
    {
        var mapped = BrowserCockpitWireMapper.ToProfileCommand(
            new BrowserProfileRequest(operation, "profile-1", "Living room"),
            epoch: 11,
            commandId: 27);

        mapped.ShouldBe(new BrowserProfileCommandMessage(11, 27, expected, "profile-1", "Living room"));
    }

    [Fact]
    public void ReceiverLibraryRequest_PreservesReplayIdentifiersForTheDeviceStore()
    {
        var mapped = BrowserCockpitWireMapper.ToDeviceLibraryRequest(new BrowserLibraryCommandMessage(
            12,
            31,
            BrowserLibraryAction.AddBookmark,
            "https://example.test/",
            "Example"));

        mapped.ShouldBe(new BrowserLibraryRequest(
            BrowserLibraryOperation.AddBookmark,
            "https://example.test/",
            "Example",
            Epoch: 12,
            CommandId: 31));
    }

    [Fact]
    public void DeviceSnapshot_SeparatesKindsAndPreservesFaviconAndVisitTime()
    {
        var mapped = BrowserCockpitWireMapper.ToLibraryState(new BrowserLibrarySnapshot(
            13,
            8,
            [
                new BrowserLibraryItem(
                    BrowserLibraryItemKind.History,
                    "https://history.test/",
                    "History",
                    DateTimeOffset.UnixEpoch.AddMilliseconds(20),
                    FaviconId: 4),
                new BrowserLibraryItem(
                    BrowserLibraryItemKind.Bookmark,
                    "https://saved.test/",
                    "Saved",
                    DateTimeOffset.UnixEpoch.AddMilliseconds(10),
                    FaviconId: 3),
            ]));

        mapped.Epoch.ShouldBe(13);
        mapped.Revision.ShouldBe(8);
        mapped.Bookmarks.Single().ShouldBe(new BrowserLibraryEntry(
            BrowserLibraryEntryKind.Bookmark,
            3,
            10,
            "https://saved.test/",
            "Saved"));
        mapped.History.Single().ShouldBe(new BrowserLibraryEntry(
            BrowserLibraryEntryKind.History,
            4,
            20,
            "https://history.test/",
            "History"));
    }

    [Fact]
    public void WorkspaceState_MapsLayoutsCapabilitiesAndPaneIds()
    {
        var mapped = BrowserCockpitWireMapper.ToWorkspace(new BrowserWorkspaceStateMessage(
            4,
            140,
            BrowserWorkspaceWireLayout.TwoColumns,
            FocusedPaneId: 42,
            BrowserWorkspaceWireInteractionMode.Page,
            PageFullscreenPaneId: 0,
            TheaterPaneId: 0,
            MaxLiveRenderers: 2,
            MaxOpenPanes: 4,
            ValueList<BrowserWorkspacePaneStateEntry>.From(
            [
                new BrowserWorkspacePaneStateEntry(
                    42,
                    0,
                    BrowserWorkspaceWirePaneResidency.Live,
                    "https://example.test/",
                    "Example",
                    Loading: false,
                    100,
                    CanGoBack: true,
                    CanGoForward: false,
                    DesiredMuted: false,
                    BrowserWorkspaceWireMuteApplication.NotRequested,
                    BrowserWorkspaceWireObservedPlayback.Playing),
            ])));

        mapped.Layout.ShouldBe(BrowserWorkspaceLayout.TwoColumns);
        mapped.FocusedPaneId.ShouldBe("42");
        mapped.Panes.Single().PaneId.ShouldBe("42");
        mapped.Capabilities.IsAvailable.ShouldBeTrue();
        mapped.Capabilities.MaximumVisiblePanes.ShouldBe(4);
        mapped.Capabilities.SupportedLayouts.ShouldBe(
            BrowserWorkspaceLayoutSet.Single |
            BrowserWorkspaceLayoutSet.TwoColumns |
            BrowserWorkspaceLayoutSet.TwoRows |
            BrowserWorkspaceLayoutSet.FourGrid);
        mapped.Capabilities.CanSendFocusedPaneInput.ShouldBeTrue();
        mapped.Capabilities.CanRequestPaneFocus.ShouldBeTrue();
    }

    [Theory]
    [InlineData(BrowserWorkspaceLayout.Single, BrowserWorkspaceWireLayout.Single)]
    [InlineData(BrowserWorkspaceLayout.TwoColumns, BrowserWorkspaceWireLayout.TwoColumns)]
    [InlineData(BrowserWorkspaceLayout.TwoRows, BrowserWorkspaceWireLayout.TwoRows)]
    [InlineData(BrowserWorkspaceLayout.FourGrid, BrowserWorkspaceWireLayout.FourGrid)]
    public void WorkspaceLayoutCommand_MapsEveryLayout(
        BrowserWorkspaceLayout layout,
        BrowserWorkspaceWireLayout expected)
    {
        var mapped = BrowserCockpitWireMapper.ToWorkspaceCommand(
            new SetBrowserWorkspaceLayoutCommand(layout),
            epoch: 4,
            commandId: 131,
            expectedRevision: 140);

        mapped.ShouldBe(new BrowserWorkspaceCommandMessage(
            4,
            131,
            140,
            BrowserWorkspaceCommandAction.SetLayout,
            Value: (byte)expected));
    }

    [Fact]
    public void WorkspaceMovePaneCommand_MapsPaneAndSlot()
    {
        var mapped = BrowserCockpitWireMapper.ToWorkspaceCommand(
            new MoveBrowserWorkspacePaneCommand("42", 1),
            epoch: 4,
            commandId: 143,
            expectedRevision: 140);

        mapped.ShouldBe(new BrowserWorkspaceCommandMessage(
            4,
            143,
            140,
            BrowserWorkspaceCommandAction.MovePane,
            PaneId: 42,
            Value: 1));
    }

    [Fact]
    public void WorkspaceInputCommand_MapsFocusedPaneText()
    {
        var mapped = BrowserCockpitWireMapper.ToWorkspaceInput(
            new SendBrowserWorkspaceInputCommand("42", "hello"),
            epoch: 4,
            commandId: 132,
            expectedRevision: 140);

        mapped.ShouldBe(new BrowserWorkspaceInputMessage(
            4,
            132,
            140,
            42,
            BrowserWorkspaceInputKind.Text,
            Text: "hello"));
    }
}
