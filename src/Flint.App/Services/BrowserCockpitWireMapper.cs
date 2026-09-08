using Flint.Protocol;
using System.Globalization;
using Flint.App.ViewModels;

namespace Flint.App.Services;

/// <summary>Pure translations between bounded wire records and cockpit-owned UI models.</summary>
internal static class BrowserCockpitWireMapper
{
    public static BrowserLibraryRequest ToDeviceLibraryRequest(BrowserLibraryCommandMessage message) =>
        new(
            message.Action switch
            {
                BrowserLibraryAction.AddBookmark => BrowserLibraryOperation.AddBookmark,
                BrowserLibraryAction.RemoveBookmark => BrowserLibraryOperation.RemoveBookmark,
                BrowserLibraryAction.ClearHistory => BrowserLibraryOperation.ClearHistory,
                BrowserLibraryAction.ClearBookmarks => BrowserLibraryOperation.ClearBookmarks,
                BrowserLibraryAction.RequestSnapshot => BrowserLibraryOperation.RequestSnapshot,
                _ => throw new ArgumentOutOfRangeException(nameof(message)),
            },
            message.Url,
            message.Title,
            message.Epoch,
            message.CommandId);

    public static BrowserLibraryStateMessage ToLibraryState(BrowserLibrarySnapshot snapshot) =>
        new(
            snapshot.Epoch,
            snapshot.Revision,
            ValueList<BrowserLibraryEntry>.From(snapshot.Entries
                .Where(entry => entry.Kind == BrowserLibraryItemKind.Bookmark)
                .Select(entry => ToWireEntry(entry, BrowserLibraryEntryKind.Bookmark))),
            ValueList<BrowserLibraryEntry>.From(snapshot.Entries
                .Where(entry => entry.Kind == BrowserLibraryItemKind.History)
                .Select(entry => ToWireEntry(entry, BrowserLibraryEntryKind.History))));

    public static BrowserProfilesSnapshot ToProfiles(BrowserProfileStateMessage message) =>
        new(
            message.Epoch,
            message.Revision,
            message.ActiveSource == BrowserProfileSource.Device
                ? BrowserProfileStorageLocation.WindowsDevice
                : BrowserProfileStorageLocation.Television,
            message.ActiveProfileId,
            message.DeviceName,
            [.. message.Profiles.Select(profile => new BrowserTvProfile(profile.ProfileId, profile.Name))]);

    public static BrowserProfileCommandMessage ToProfileCommand(
        BrowserProfileRequest request,
        long epoch,
        long commandId) =>
        new(
            epoch,
            commandId,
            request.Operation switch
            {
                BrowserProfileOperation.SelectTvProfile => BrowserProfileAction.SelectTvProfile,
                BrowserProfileOperation.CreateTvProfile => BrowserProfileAction.CreateTvProfile,
                BrowserProfileOperation.RenameTvProfile => BrowserProfileAction.RenameTvProfile,
                BrowserProfileOperation.DeleteTvProfile => BrowserProfileAction.DeleteTvProfile,
                BrowserProfileOperation.SelectWindowsDevice => BrowserProfileAction.SelectDevice,
                BrowserProfileOperation.RequestSnapshot => BrowserProfileAction.RequestSnapshot,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            },
            request.ProfileId,
            request.Name);

    public static BrowserNetworkCommandMessage ToNetworkCommand(
        BrowserNetworkRequest request,
        long epoch,
        long commandId) =>
        new(
            epoch,
            commandId,
            request.Operation switch
            {
                BrowserNetworkOperation.Set => BrowserNetworkAction.Set,
                BrowserNetworkOperation.Clear => BrowserNetworkAction.Clear,
                BrowserNetworkOperation.RequestSnapshot => BrowserNetworkAction.RequestSnapshot,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            },
            request.ProfileId,
            request.VpnEnabled,
            request.Provider switch
            {
                BrowserVpnProviderKind.WireGuard => BrowserVpnProvider.WireGuard,
                _ => BrowserVpnProvider.None,
            },
            request.AutoConnectOnBrowserStart,
            request.RequireVpnBeforeBrowse,
            request.ConfigText);

    public static BrowserNetworkSnapshot ToNetwork(BrowserNetworkStateMessage message) =>
        new(
            message.Epoch,
            message.Revision,
            message.ProfileId,
            message.VpnEnabled,
            message.Provider == BrowserVpnProvider.WireGuard
                ? BrowserVpnProviderKind.WireGuard
                : BrowserVpnProviderKind.None,
            message.AutoConnectOnBrowserStart,
            message.RequireVpnBeforeBrowse,
            message.ConfigPresent,
            message.CapabilityPreparable,
            message.CapabilityReason,
            message.SessionState switch
            {
                BrowserVpnSessionState.NeedsConsent => BrowserVpnSessionKind.NeedsConsent,
                BrowserVpnSessionState.Connecting => BrowserVpnSessionKind.Connecting,
                BrowserVpnSessionState.Connected => BrowserVpnSessionKind.Connected,
                BrowserVpnSessionState.Failed => BrowserVpnSessionKind.Failed,
                BrowserVpnSessionState.Unavailable => BrowserVpnSessionKind.Unavailable,
                _ => BrowserVpnSessionKind.Idle,
            },
            message.SessionDetail);

    public static BrowserWorkspaceSnapshot ToWorkspace(BrowserWorkspaceStateMessage message) =>
        new(
            message.Epoch,
            message.Revision,
            ToWorkspaceCapabilities(message),
            ToLayout(message.Layout),
            ToFocusedPaneId(message.FocusedPaneId),
            [.. message.Panes.Select(pane => ToWorkspacePane(pane, message))]);

    public static BrowserWorkspaceCommandMessage ToWorkspaceCommand(
        BrowserWorkspaceCommand command,
        long epoch,
        long commandId,
        long expectedRevision) =>
        command switch
        {
            FocusBrowserWorkspacePaneCommand focus => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                BrowserWorkspaceCommandAction.Focus,
                RequirePaneId(focus.PaneId)),
            SetBrowserWorkspaceLayoutCommand layout => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                BrowserWorkspaceCommandAction.SetLayout,
                Value: (byte)ToWireLayout(layout.Layout)),
            CreateBrowserWorkspacePaneCommand create => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                BrowserWorkspaceCommandAction.OpenPane, Url: create.Url),
            CloseBrowserWorkspacePaneCommand close => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                BrowserWorkspaceCommandAction.ClosePane,
                RequirePaneId(close.PaneId)),
            MoveBrowserWorkspacePaneCommand move => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                BrowserWorkspaceCommandAction.MovePane,
                RequirePaneId(move.PaneId),
                Value: (byte)Math.Clamp(move.TargetSlot, 0, 3)),
            RequestBrowserWorkspaceMediaCommand media =>
                ToWorkspaceMediaCommand(media, epoch, commandId, expectedRevision),
            BrowserWorkspacePageCommand page => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                page.Action switch
                {
                    BrowserWorkspacePageAction.Navigate => BrowserWorkspaceCommandAction.Navigate,
                    BrowserWorkspacePageAction.Reload => BrowserWorkspaceCommandAction.Reload,
                    BrowserWorkspacePageAction.Back => BrowserWorkspaceCommandAction.Back,
                    BrowserWorkspacePageAction.Forward => BrowserWorkspaceCommandAction.Forward,
                    _ => throw new ArgumentOutOfRangeException(nameof(command)),
                }, RequirePaneId(page.PaneId), Url: page.Url),
            BrowserWorkspaceInteractionCommand interaction => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                BrowserWorkspaceCommandAction.SetInteraction,
                Value: (byte)(interaction.InteractWithPage ? BrowserWorkspaceWireInteractionMode.Page : BrowserWorkspaceWireInteractionMode.WorkspaceChrome)),
            RefreshBrowserWorkspaceCommand => new BrowserWorkspaceCommandMessage(
                epoch,
                commandId,
                expectedRevision,
                BrowserWorkspaceCommandAction.RequestSnapshot),
            SendBrowserWorkspaceInputCommand => throw new InvalidOperationException(
                "Workspace text input uses BrowserWorkspaceInputMessage, not a command."),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };

    public static BrowserWorkspaceInputMessage ToWorkspaceInput(
        SendBrowserWorkspaceInputCommand command,
        long epoch,
        long commandId,
        long expectedRevision) =>
        new(
            epoch,
            commandId,
            expectedRevision,
            RequirePaneId(command.PaneId),
            BrowserWorkspaceInputKind.Text,
            Text: command.Text);

    public static BrowserWorkspaceInputMessage ToWorkspaceKey(SendBrowserWorkspaceKeyCommand command,
        long epoch, long commandId, long expectedRevision) =>
        new(epoch, commandId, expectedRevision, RequirePaneId(command.PaneId),
            BrowserWorkspaceInputKind.Key, Key: command.Key);

    private static BrowserWorkspaceCommandMessage ToWorkspaceMediaCommand(
        RequestBrowserWorkspaceMediaCommand command,
        long epoch,
        long commandId,
        long expectedRevision)
    {
        var paneId = RequirePaneId(command.PaneId);
        return command.Action switch
        {
            BrowserWorkspaceMediaActions.Play or
            BrowserWorkspaceMediaActions.Pause or
            BrowserWorkspaceMediaActions.TogglePlayback =>
                new BrowserWorkspaceCommandMessage(
                    epoch,
                    commandId,
                    expectedRevision,
                    BrowserWorkspaceCommandAction.PlayPause,
                    paneId),
            BrowserWorkspaceMediaActions.Mute =>
                new BrowserWorkspaceCommandMessage(
                    epoch,
                    commandId,
                    expectedRevision,
                    BrowserWorkspaceCommandAction.SetMute,
                    paneId,
                    Value: 1),
            BrowserWorkspaceMediaActions.Unmute =>
                new BrowserWorkspaceCommandMessage(
                    epoch,
                    commandId,
                    expectedRevision,
                    BrowserWorkspaceCommandAction.SetMute,
                    paneId,
                    Value: 0),
            BrowserWorkspaceMediaActions.ToggleMute =>
                new BrowserWorkspaceCommandMessage(
                    epoch,
                    commandId,
                    expectedRevision,
                    BrowserWorkspaceCommandAction.SetMute,
                    paneId,
                    Value: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    private static BrowserWorkspaceCapabilities ToWorkspaceCapabilities(BrowserWorkspaceStateMessage message)
    {
        var supportedLayouts = BrowserWorkspaceLayoutSet.None;
        if (message.MaxOpenPanes >= 1)
        {
            supportedLayouts |= BrowserWorkspaceLayoutSet.Single;
        }

        if (message.MaxOpenPanes >= 2)
        {
            supportedLayouts |= BrowserWorkspaceLayoutSet.TwoColumns | BrowserWorkspaceLayoutSet.TwoRows;
        }

        if (message.MaxOpenPanes >= 4)
        {
            supportedLayouts |= BrowserWorkspaceLayoutSet.FourGrid;
        }

        var paneCount = message.Panes.Count;
        var hasControllableMedia = message.Panes.Any(pane =>
            pane.Residency == BrowserWorkspaceWirePaneResidency.Live &&
            pane.ObservedPlayback != BrowserWorkspaceWireObservedPlayback.Unavailable);

        return new BrowserWorkspaceCapabilities(
            IsAvailable: message.MaxOpenPanes > 0,
            MaximumVisiblePanes: message.MaxOpenPanes,
            SupportedLayouts: supportedLayouts,
            CanCreatePane: paneCount < message.MaxOpenPanes,
            CanClosePane: paneCount > 1,
            // Focus must work in both chrome and page interaction modes. Gating on WorkspaceChrome
            // alone left every pane tile dead while the TV was in Page mode (2026-09-08).
            CanRequestPaneFocus: message.MaxOpenPanes > 0,
            CanSendFocusedPaneInput: message.MaxOpenPanes > 0,
            CanRequestMediaControl: hasControllableMedia || message.Panes.Any(pane =>
                pane.Residency == BrowserWorkspaceWirePaneResidency.Live &&
                pane.MuteApplication == BrowserWorkspaceWireMuteApplication.AppliedToRenderer),
            CanRequestTheaterMode: false);
    }

    private static BrowserWorkspacePaneSnapshot ToWorkspacePane(
        BrowserWorkspacePaneStateEntry pane,
        BrowserWorkspaceStateMessage message) =>
        new(
            pane.PaneId.ToString(CultureInfo.InvariantCulture),
            pane.Slot,
            pane.Url,
            pane.Title,
            pane.Progress,
            ToPaneState(pane),
            ToMediaSnapshot(pane),
            IsPageFullscreen: message.PageFullscreenPaneId == pane.PaneId,
            CanGoBack: pane.CanGoBack,
            CanGoForward: pane.CanGoForward);

    private static BrowserWorkspacePaneState ToPaneState(BrowserWorkspacePaneStateEntry pane)
    {
        if (pane.Loading)
        {
            return BrowserWorkspacePaneState.Loading;
        }

        return pane.Residency switch
        {
            BrowserWorkspaceWirePaneResidency.Live => BrowserWorkspacePaneState.Live,
            BrowserWorkspaceWirePaneResidency.Suspended => BrowserWorkspacePaneState.Suspended,
            BrowserWorkspaceWirePaneResidency.Failed => BrowserWorkspacePaneState.Failed,
            _ => BrowserWorkspacePaneState.Unknown,
        };
    }

    private static BrowserWorkspaceMediaSnapshot ToMediaSnapshot(BrowserWorkspacePaneStateEntry pane)
    {
        if (pane.Residency != BrowserWorkspaceWirePaneResidency.Live)
        {
            return BrowserWorkspaceMediaSnapshot.None;
        }

        var playback = pane.ObservedPlayback switch
        {
            BrowserWorkspaceWireObservedPlayback.Playing => BrowserWorkspacePlaybackState.Playing,
            BrowserWorkspaceWireObservedPlayback.Paused => BrowserWorkspacePlaybackState.Paused,
            BrowserWorkspaceWireObservedPlayback.Ended => BrowserWorkspacePlaybackState.Paused,
            _ => BrowserWorkspacePlaybackState.Unknown,
        };

        var mute = pane.MuteApplication switch
        {
            BrowserWorkspaceWireMuteApplication.AppliedToRenderer =>
                pane.DesiredMuted ? BrowserWorkspaceMuteState.Muted : BrowserWorkspaceMuteState.Audible,
            _ => BrowserWorkspaceMuteState.Unknown,
        };

        var allowed = BrowserWorkspaceMediaActions.None;
        if (pane.ObservedPlayback != BrowserWorkspaceWireObservedPlayback.Unavailable)
        {
            // The current wire operation is a toggle, even when a previous observation is known.
            allowed |= BrowserWorkspaceMediaActions.TogglePlayback;
        }

        if (pane.MuteApplication == BrowserWorkspaceWireMuteApplication.AppliedToRenderer)
        {
            allowed |= mute switch
            {
                BrowserWorkspaceMuteState.Muted => BrowserWorkspaceMediaActions.Unmute,
                BrowserWorkspaceMuteState.Audible => BrowserWorkspaceMediaActions.Mute,
                _ => BrowserWorkspaceMediaActions.ToggleMute,
            };
        }

        return new BrowserWorkspaceMediaSnapshot(playback, mute, allowed);
    }

    private static BrowserWorkspaceLayout ToLayout(BrowserWorkspaceWireLayout layout) => layout switch
    {
        BrowserWorkspaceWireLayout.Single => BrowserWorkspaceLayout.Single,
        BrowserWorkspaceWireLayout.TwoColumns => BrowserWorkspaceLayout.TwoColumns,
        BrowserWorkspaceWireLayout.TwoRows => BrowserWorkspaceLayout.TwoRows,
        BrowserWorkspaceWireLayout.FourGrid => BrowserWorkspaceLayout.FourGrid,
        _ => BrowserWorkspaceLayout.Single,
    };

    private static BrowserWorkspaceWireLayout ToWireLayout(BrowserWorkspaceLayout layout) => layout switch
    {
        BrowserWorkspaceLayout.Single => BrowserWorkspaceWireLayout.Single,
        BrowserWorkspaceLayout.TwoColumns => BrowserWorkspaceWireLayout.TwoColumns,
        BrowserWorkspaceLayout.TwoRows => BrowserWorkspaceWireLayout.TwoRows,
        BrowserWorkspaceLayout.FourGrid => BrowserWorkspaceWireLayout.FourGrid,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    private static string? ToFocusedPaneId(long focusedPaneId) =>
        focusedPaneId > 0 ? focusedPaneId.ToString(CultureInfo.InvariantCulture) : null;

    private static long RequirePaneId(string paneId)
    {
        if (!long.TryParse(paneId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wirePaneId) ||
            wirePaneId <= 0)
        {
            throw new ArgumentException("Workspace pane ID is not a positive wire identifier.", nameof(paneId));
        }

        return wirePaneId;
    }

    private static BrowserLibraryEntry ToWireEntry(
        BrowserLibraryItem entry,
        BrowserLibraryEntryKind kind) =>
        new(
            kind,
            entry.FaviconId,
            Math.Max(0, entry.LastVisited.ToUnixTimeMilliseconds()),
            entry.Url,
            entry.Title);
}
