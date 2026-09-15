using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Desktop-only workspace safety rules. Protocol binding comes later; these tests pin the user
/// experience the binding must preserve rather than testing an invented wire format early.
/// </summary>
public sealed class BrowserWorkspaceViewModelTests
{
    [Fact]
    public async Task GeometryAtMatchingRevisionEnablesResizeAndModeWithoutReplacingPanes()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        var snapshot = Snapshot(1, "one", [Pane("one", 0), Pane("two", 1)], BrowserWorkspaceLayout.TwoColumns);
        workspace.ApplySnapshot(snapshot);
        workspace.CanResize.ShouldBeFalse();
        workspace.ApplySnapshot(snapshot with { CanResize = true, SupportsCustomization = true, ColumnSplit = 7000 });
        workspace.ColumnSplit.ShouldBe(7000);
        workspace.CanResize.ShouldBeTrue();
        await workspace.ResizeAsync(99999, -1);
        sink.Commands.Last().ShouldBe(new ResizeBrowserWorkspaceCommand(8500, 1500));
        workspace.ApplySnapshot(snapshot with { SupportsCustomization = true, IsWorkspaceMode = false });
        workspace.IsWorkspaceMode.ShouldBeFalse();
        workspace.Panes.Count.ShouldBe(2);
        workspace.Bind(null);
        workspace.CanResize.ShouldBeFalse();
        workspace.CanSwitchMode.ShouldBeFalse();
    }

    [Fact]
    public void NewWorkspace_IsUnavailableAndDoesNotPretendItHasPanes()
    {
        var workspace = new BrowserWorkspaceViewModel();

        workspace.IsAvailable.ShouldBeFalse();
        workspace.Panes.ShouldBeEmpty();
        workspace.StatusLabel.ShouldBe("WORKSPACE UNAVAILABLE");
        workspace.AddPaneCommand.CanExecute(null).ShouldBeFalse();
        workspace.SendTextCommand.CanExecute(null).ShouldBeFalse();
        workspace.LayoutOptions.All(option => !option.SelectCommand.CanExecute(null)).ShouldBeTrue();
    }

    [Fact]
    public void SnapshotWithoutABoundChannel_ShowsReceiverStateButKeepsCommandsDisabled()
    {
        var workspace = new BrowserWorkspaceViewModel();

        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            panes: [Pane("one", slot: 0)]));

        workspace.IsAvailable.ShouldBeTrue();
        workspace.Panes.Count.ShouldBe(1);
        workspace.Panes.Single().CanSendInput.ShouldBeFalse();
        workspace.Panes.Single().RequestFocusCommand.CanExecute(null).ShouldBeFalse();
        workspace.Panes.Single().RequestPlaybackCommand.CanExecute(null).ShouldBeFalse();
        workspace.AddPaneCommand.CanExecute(null).ShouldBeFalse();
        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns)
            .SelectCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task FocusRequest_BlocksAllTextUntilTheTvConfirmsTheExactPane()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            panes: [Pane("one", 0), Pane("two", 1)]));
        var first = workspace.Panes.Single(pane => pane.PaneId == "one");
        var second = workspace.Panes.Single(pane => pane.PaneId == "two");

        first.CanSendInput.ShouldBeTrue();
        second.CanSendInput.ShouldBeFalse();
        await second.RequestFocusCommand.ExecuteAsync(null);

        sink.Commands.ShouldHaveSingleItem().ShouldBe(new FocusBrowserWorkspacePaneCommand("two"));
        workspace.PendingFocusPaneId.ShouldBe("two");
        workspace.CanSendConfirmedInput.ShouldBeFalse();
        first.CanSendInput.ShouldBeFalse();
        second.CanSendInput.ShouldBeFalse();
        workspace.InputStatusLabel.ShouldContain("INPUT BLOCKED");

        // An old packet cannot release the barrier to the previous page.
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            panes: [Pane("one", 0), Pane("two", 1)])).ShouldBeFalse();
        workspace.PendingFocusPaneId.ShouldBe("two");
        first.CanSendInput.ShouldBeFalse();

        workspace.ApplySnapshot(Snapshot(
            revision: 2,
            focusedPaneId: "two",
            panes: [Pane("one", 0), Pane("two", 1)]));

        workspace.PendingFocusPaneId.ShouldBeNull();
        workspace.ConfirmedFocusedPaneId.ShouldBe("two");
        workspace.FocusedPane.ShouldBeSameAs(second);
        first.CanSendInput.ShouldBeFalse();
        second.CanSendInput.ShouldBeTrue();
        workspace.DraftInputText = "search phrase";
        workspace.SendTextCommand.CanExecute(null).ShouldBeTrue();
        await workspace.SendTextCommand.ExecuteAsync(null);

        sink.Commands.Last().ShouldBe(new SendBrowserWorkspaceInputCommand("two", "search phrase"));
        workspace.DraftInputText.ShouldBeEmpty();
    }

    [Fact]
    public async Task FailedFocusSend_ReleasesTheBarrierWithoutRetargetingInput()
    {
        var sink = new RecordingSink { ThrowOnSend = true };
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            panes: [Pane("one", 0), Pane("two", 1)]));
        var second = workspace.Panes.Single(pane => pane.PaneId == "two");

        await second.RequestFocusCommand.ExecuteAsync(null);

        workspace.PendingFocusPaneId.ShouldBeNull();
        workspace.ConfirmedFocusedPaneId.ShouldBe("one");
        workspace.Panes.Single(pane => pane.PaneId == "one").CanSendInput.ShouldBeTrue();
        workspace.CommandError.ShouldNotBeNull().ShouldContain("FOCUS REQUEST");
    }

    [Fact]
    public async Task LayoutControls_AreCapabilityGatedAndDoNotOptimisticallyChangeTheArrangement()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));
        var sideBySide = workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns);
        var fourGrid = workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.FourGrid);

        sideBySide.IsSupported.ShouldBeTrue();
        sideBySide.SelectCommand.CanExecute(null).ShouldBeTrue();
        fourGrid.IsSupported.ShouldBeFalse();
        fourGrid.AvailabilityLabel.ShouldContain("NOT SUPPORTED");
        fourGrid.SelectCommand.CanExecute(null).ShouldBeFalse();

        await sideBySide.SelectCommand.ExecuteAsync(null);

        sink.Commands.ShouldHaveSingleItem().ShouldBe(
            new SetBrowserWorkspaceLayoutCommand(BrowserWorkspaceLayout.TwoColumns));
        workspace.Layout.ShouldBe(BrowserWorkspaceLayout.Single);
        workspace.IsLayoutRequestPending.ShouldBeTrue();

        workspace.ApplySnapshot(Snapshot(
            revision: 2,
            focusedPaneId: "one",
            layout: BrowserWorkspaceLayout.TwoColumns,
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));

        workspace.Layout.ShouldBe(BrowserWorkspaceLayout.TwoColumns);
        workspace.IsLayoutRequestPending.ShouldBeFalse();
    }

    [Fact]
    public void SideBySide_DisabledUntilASecondPaneIsOpen()
    {
        var workspace = new BrowserWorkspaceViewModel(new RecordingSink());
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0)]));

        var sideBySide = workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns);
        sideBySide.IsSupported.ShouldBeFalse();
        sideBySide.SelectCommand.CanExecute(null).ShouldBeFalse();
        sideBySide.AvailabilityLabel.ShouldContain("SECOND PANE");

        workspace.ApplySnapshot(Snapshot(
            revision: 2,
            focusedPaneId: "one",
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));

        sideBySide.IsSupported.ShouldBeTrue();
        sideBySide.SelectCommand.CanExecute(null).ShouldBeTrue();
        sideBySide.AvailabilityLabel.ShouldContain("AVAILABLE");
    }

    [Fact]
    public async Task SplitBeside_WithOnePane_CreatesSecondThenRequestsLayout()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0)]));

        workspace.CanSplitBeside.ShouldBeTrue();
        workspace.StatusLabel.ShouldContain("SPLIT VIEW");

        await workspace.SplitBesideCommand.ExecuteAsync(null);

        sink.Commands.ShouldHaveSingleItem().ShouldBe(new CreateBrowserWorkspacePaneCommand());
        workspace.IsPaneCreationPending.ShouldBeTrue();
        workspace.StatusLabel.ShouldContain("OPENING PAGES");

        workspace.ApplySnapshot(Snapshot(
            revision: 2,
            focusedPaneId: "one",
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));

        sink.Commands.Count.ShouldBe(2);
        sink.Commands[1].ShouldBe(new SetBrowserWorkspaceLayoutCommand(BrowserWorkspaceLayout.TwoColumns));
        workspace.IsLayoutRequestPending.ShouldBeTrue();
        workspace.IsPaneCreationPending.ShouldBeFalse();
    }

    [Fact]
    public async Task GuidedSplit_TimesOutWhenTvStaysOnTabs()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: null,
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: []));

        await workspace.SplitBesideCommand.ExecuteAsync(null);
        workspace.IsPaneCreationPending.ShouldBeTrue();
        workspace.StatusLabel.ShouldContain("MOSAIC");

        workspace.SetPendingWaitStartedUtcForTests(
            DateTime.UtcNow - BrowserWorkspaceViewModel.PendingArrangementTimeout - TimeSpan.FromSeconds(1));

        workspace.ObserveHostActivityWhilePending();

        workspace.IsPaneCreationPending.ShouldBeFalse();
        workspace.CanSplitBeside.ShouldBeTrue();
        workspace.CommandError.ShouldNotBeNull();
        workspace.CommandError.ShouldContain("TIMED OUT");
        workspace.StatusLabel.ShouldContain("SPLIT VIEW");
    }

    [Fact]
    public async Task SplitBeside_WithTwoPanesAlreadyOpen_RequestsLayoutOnly()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));

        await workspace.SplitBesideCommand.ExecuteAsync(null);

        sink.Commands.ShouldHaveSingleItem().ShouldBe(
            new SetBrowserWorkspaceLayoutCommand(BrowserWorkspaceLayout.TwoColumns));
        workspace.IsLayoutRequestPending.ShouldBeTrue();
    }

    [Fact]
    public void EmptyWorkspace_DisablesEveryMultiPaneLayout()
    {
        var workspace = new BrowserWorkspaceViewModel(new RecordingSink());
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: null,
            layouts: BrowserWorkspaceLayoutSet.Single
                | BrowserWorkspaceLayoutSet.TwoColumns
                | BrowserWorkspaceLayoutSet.TwoRows
                | BrowserWorkspaceLayoutSet.FourGrid,
            maximumVisiblePanes: 4,
            panes: []));

        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.Single)
            .SelectCommand.CanExecute(null).ShouldBeFalse(); // already selected Single
        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns)
            .SelectCommand.CanExecute(null).ShouldBeFalse();
        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoRows)
            .SelectCommand.CanExecute(null).ShouldBeFalse();
        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.FourGrid)
            .SelectCommand.CanExecute(null).ShouldBeFalse();
        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns)
            .AvailabilityLabel.ShouldContain("SECOND PANE");
    }

    [Theory]
    [InlineData(0, BrowserWorkspaceLayout.Single, true)]
    [InlineData(0, BrowserWorkspaceLayout.TwoColumns, false)]
    [InlineData(1, BrowserWorkspaceLayout.Single, true)]
    [InlineData(1, BrowserWorkspaceLayout.TwoColumns, false)]
    [InlineData(2, BrowserWorkspaceLayout.TwoColumns, true)]
    [InlineData(2, BrowserWorkspaceLayout.TwoRows, true)]
    [InlineData(2, BrowserWorkspaceLayout.FourGrid, false)]
    [InlineData(3, BrowserWorkspaceLayout.FourGrid, true)]
    [InlineData(4, BrowserWorkspaceLayout.FourGrid, true)]
    [InlineData(4, BrowserWorkspaceLayout.Single, false)]
    public void FitsPaneCount_MatchesTelevisionReducer(int paneCount, BrowserWorkspaceLayout layout, bool expected) =>
        layout.FitsPaneCount(paneCount).ShouldBe(expected);

    [Fact]
    public async Task AddPane_WaitsForAReceiverSnapshotAndRespectsAdvertisedCapacity()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0)]));

        workspace.CanAddPane.ShouldBeTrue();
        await workspace.AddPaneCommand.ExecuteAsync(null);

        sink.Commands.ShouldHaveSingleItem().ShouldBeOfType<CreateBrowserWorkspacePaneCommand>();
        workspace.Panes.Count.ShouldBe(1);
        workspace.IsPaneCreationPending.ShouldBeTrue();
        workspace.CanAddPane.ShouldBeFalse();

        workspace.ApplySnapshot(Snapshot(
            revision: 2,
            focusedPaneId: "one",
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));

        workspace.IsPaneCreationPending.ShouldBeFalse();
        workspace.Panes.Count.ShouldBe(2);
        workspace.CanAddPane.ShouldBeFalse();
    }

    [Fact]
    public async Task MediaButton_SendsAPerPaneRequestWithoutPretendingThePagePaused()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            panes:
            [
                Pane(
                    "one",
                    0,
                    media: new BrowserWorkspaceMediaSnapshot(
                        BrowserWorkspacePlaybackState.Playing,
                        BrowserWorkspaceMuteState.Audible,
                        BrowserWorkspaceMediaActions.Pause | BrowserWorkspaceMediaActions.Mute)),
            ]));
        var pane = workspace.Panes.Single();

        pane.PlaybackRequestLabel.ShouldBe("REQUEST PAUSE");
        pane.ReportedMediaLabel.ShouldBe("PLAYING");
        await pane.RequestPlaybackCommand.ExecuteAsync(null);

        sink.Commands.ShouldHaveSingleItem().ShouldBe(
            new RequestBrowserWorkspaceMediaCommand("one", BrowserWorkspaceMediaActions.Pause));
        pane.IsMediaRequestPending.ShouldBeTrue();
        pane.ReportedMediaLabel.ShouldBe("PLAYING");
        pane.MediaRequestStatusLabel.ShouldContain("WAITING FOR TV");

        // An updated receiver snapshot that still says playing is evidence, not a reason to claim
        // success just because the desktop button was pressed.
        workspace.ApplySnapshot(Snapshot(
            revision: 2,
            focusedPaneId: "one",
            panes:
            [
                Pane(
                    "one",
                    0,
                    media: new BrowserWorkspaceMediaSnapshot(
                        BrowserWorkspacePlaybackState.Playing,
                        BrowserWorkspaceMuteState.Audible,
                        BrowserWorkspaceMediaActions.Pause | BrowserWorkspaceMediaActions.Mute)),
            ]));

        pane.IsMediaRequestPending.ShouldBeFalse();
        pane.MediaRequestStatusLabel.ShouldContain("NOT CONFIRMED");
        pane.ReportedMediaLabel.ShouldBe("PLAYING");
    }

    [Fact]
    public async Task LayoutRequest_SurvivesProgressSnapshotsUntilConfirmed()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));
        var sideBySide = workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns);

        await sideBySide.SelectCommand.ExecuteAsync(null);
        workspace.IsLayoutRequestPending.ShouldBeTrue();

        // Progress-only snapshots must not clear the pending layout gate.
        workspace.ApplySnapshot(Snapshot(
            revision: 2,
            focusedPaneId: "one",
            layout: BrowserWorkspaceLayout.Single,
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));

        workspace.IsLayoutRequestPending.ShouldBeTrue();
        workspace.CommandError.ShouldBeNull();

        workspace.ApplySnapshot(Snapshot(
            revision: 3,
            focusedPaneId: "one",
            layout: BrowserWorkspaceLayout.TwoColumns,
            layouts: BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0), Pane("two", 1)]));

        workspace.IsLayoutRequestPending.ShouldBeFalse();
        workspace.Layout.ShouldBe(BrowserWorkspaceLayout.TwoColumns);
    }

    [Fact]
    public async Task AddPane_ClearsPendingAfterUnconfirmedSnapshotsSoControlsAreNotBricked()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "one",
            maximumVisiblePanes: 2,
            panes: [Pane("one", 0)]));

        await workspace.AddPaneCommand.ExecuteAsync(null);
        workspace.IsPaneCreationPending.ShouldBeTrue();
        workspace.CanAddPane.ShouldBeFalse();

        for (var revision = 2; revision <= BrowserWorkspaceViewModel.PendingConfirmationMissLimit + 1; revision++)
        {
            workspace.ApplySnapshot(Snapshot(
                revision: revision,
                focusedPaneId: "one",
                maximumVisiblePanes: 2,
                panes: [Pane("one", 0)]));
        }

        workspace.IsPaneCreationPending.ShouldBeFalse();
        workspace.CanAddPane.ShouldBeTrue();
        workspace.CommandError.ShouldNotBeNull().ShouldContain("DID NOT OPEN");
    }

    [Fact]
    public void ReceiverWideMediaCapability_DisablesPerPaneButtonsEvenWhenAPageReportsActions()
    {
        var sink = new RecordingSink();
        var capabilities = Capabilities() with { CanRequestMediaControl = false };
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(new BrowserWorkspaceSnapshot(
            Epoch: 7,
            Revision: 1,
            capabilities,
            BrowserWorkspaceLayout.Single,
            "one",
            [Pane("one", 0, media: new BrowserWorkspaceMediaSnapshot(
                BrowserWorkspacePlaybackState.Playing,
                BrowserWorkspaceMuteState.Audible,
                BrowserWorkspaceMediaActions.Pause | BrowserWorkspaceMediaActions.Mute))]));

        var pane = workspace.Panes.Single();
        pane.RequestPlaybackCommand.CanExecute(null).ShouldBeFalse();
        pane.RequestMuteCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void InvalidateIfStale_DisablesLayoutUntilMatchingEpochSnapshot()
    {
        var sink = new RecordingSink();
        var workspace = new BrowserWorkspaceViewModel(sink);
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: null,
            panes: [],
            epoch: 10));

        workspace.IsAvailable.ShouldBeTrue();
        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns)
            .SelectCommand.CanExecute(null).ShouldBeFalse(); // 0 panes — need Split View / second page first

        workspace.InvalidateIfStale(11);

        workspace.IsAvailable.ShouldBeFalse();
        workspace.Panes.ShouldBeEmpty();
        workspace.LayoutOptions.All(option => !option.SelectCommand.CanExecute(null)).ShouldBeTrue();
        workspace.CommandError.ShouldBeNull();

        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: null,
            panes: [],
            epoch: 11));

        workspace.IsAvailable.ShouldBeTrue();
        workspace.LayoutOptions.Single(option => option.Layout == BrowserWorkspaceLayout.TwoColumns)
            .SelectCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void DuplicateOrUnknownPaneIds_AreNotUsedAsInputTargets()
    {
        var workspace = new BrowserWorkspaceViewModel(new RecordingSink());
        workspace.ApplySnapshot(Snapshot(
            revision: 1,
            focusedPaneId: "missing",
            panes: [Pane(" one ", 2), Pane("one", 0), Pane(string.Empty, 1)]));

        workspace.Panes.Count.ShouldBe(1);
        workspace.Panes.Single().PaneId.ShouldBe("one");
        workspace.ConfirmedFocusedPaneId.ShouldBeNull();
        workspace.CanSendConfirmedInput.ShouldBeFalse();
    }

    private static BrowserWorkspaceSnapshot Snapshot(
        long revision,
        string? focusedPaneId,
        IReadOnlyList<BrowserWorkspacePaneSnapshot> panes,
        BrowserWorkspaceLayout layout = BrowserWorkspaceLayout.Single,
        BrowserWorkspaceLayoutSet layouts = BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
        int maximumVisiblePanes = 2,
        long epoch = 7) =>
        new(
            Epoch: epoch,
            Revision: revision,
            Capabilities: Capabilities(layouts, maximumVisiblePanes),
            Layout: layout,
            FocusedPaneId: focusedPaneId,
            Panes: panes);

    private static BrowserWorkspaceCapabilities Capabilities(
        BrowserWorkspaceLayoutSet layouts = BrowserWorkspaceLayoutSet.Single | BrowserWorkspaceLayoutSet.TwoColumns,
        int maximumVisiblePanes = 2) =>
        new(
            IsAvailable: true,
            MaximumVisiblePanes: maximumVisiblePanes,
            SupportedLayouts: layouts,
            CanCreatePane: true,
            CanClosePane: true,
            CanRequestPaneFocus: true,
            CanSendFocusedPaneInput: true,
            CanRequestMediaControl: true,
            CanRequestTheaterMode: false);

    private static BrowserWorkspacePaneSnapshot Pane(
        string id,
        int slot,
        BrowserWorkspaceMediaSnapshot? media = null) =>
        new(
            PaneId: id,
            Slot: slot,
            Url: "https://example.test/",
            Title: id == "one" ? "One" : "Two",
            Progress: 100,
            State: BrowserWorkspacePaneState.Live,
            Media: media ?? BrowserWorkspaceMediaSnapshot.None);

    private sealed class RecordingSink : IBrowserWorkspaceCommandSink
    {
        public List<BrowserWorkspaceCommand> Commands { get; } = [];

        public bool ThrowOnSend { get; init; }

        public Task SendAsync(BrowserWorkspaceCommand command, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
            {
                throw new IOException("Simulated session drop");
            }

            Commands.Add(command);
            return Task.CompletedTask;
        }
    }
}
