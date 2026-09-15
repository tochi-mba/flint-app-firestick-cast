using System.Diagnostics.CodeAnalysis;
using Flint.App.Services;
using Flint.Core;

namespace Flint.App.ViewModels;

/// <summary>Applies authenticated snapshots on the UI thread for their original connection only.</summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Detach disposes the restorer and is this binding's end of life; IDisposable would add a second way to end it.")]
internal sealed class BrowserCockpitBinding(
    IBrowserUiDispatcher dispatcher,
    BrowserTabsViewModel tabs,
    BrowserViewControlsViewModel viewControls,
    BrowserLibraryViewModel library,
    BrowserNetworkViewModel network,
    BrowserWorkspaceViewModel workspace,
    Action<BrowserTabsSnapshot>? onTabsApplied = null,
    Func<BrowserTabRequest, CancellationToken, Task<bool>>? beforeTabCommand = null)
{
    private IBrowserCockpitRemote? remote;
    private BrowserCockpitFeatures appliedFeatures;
    private Action? unsubscribe;
    private long generation;
    private BrowserProfileSessionRestorer? restorer;

    public void Attach(IBrowserCockpitRemote? cockpit)
    {
        Detach();
        if (cockpit is null) return;
        remote = cockpit;
        restorer = new(cockpit, error =>
        {
            library.SyncError = error;
            library.CanRetryRestore = error is { Length: > 0 } &&
                error.Contains("restore", StringComparison.OrdinalIgnoreCase);
        }, busy => workspace.IsRestoringProfile = busy);
        var attachedGeneration = generation;
        tabs.Bind(cockpit, beforeTabCommand);
        viewControls.Bind(cockpit);
        library.Bind(cockpit);
        network.Bind(cockpit);
        ApplyFeatures(cockpit.CockpitFeatures);
        FlintDiag.Info("FlintBrowser", $"cockpit attach features={cockpit.CockpitFeatures}");

        void Dispatch(Action action) => dispatcher.Dispatch(() =>
        {
            if (generation != attachedGeneration || !ReferenceEquals(remote, cockpit)) return;
            ApplyFeatures(cockpit.CockpitFeatures);
            action();
        });
        void OnTabs(BrowserTabsSnapshot state) => Dispatch(() =>
        {
            tabs.Apply(state);
            restorer?.Observe(state);
            if (restorer?.CanCapture == true) library.CaptureTabs(state);
            workspace.InvalidateIfStale(state.Epoch);
            workspace.ObserveHostActivityWhilePending();
            onTabsApplied?.Invoke(state);
            FlintDiag.Info("FlintBrowser", $"tabs snapshot count={state.Tabs.Count} active={state.ActiveTabId}");
        });
        void OnView(BrowserViewSnapshot state) => Dispatch(() => viewControls.Apply(state));
        void OnLibrary(BrowserLibrarySnapshot state) => Dispatch(() => library.Apply(state));
        void OnDeviceRequest(BrowserLibraryRequest request) => Dispatch(() => _ = library.HandleDeviceRequestAsync(request));
        void OnProfiles(BrowserProfilesSnapshot state) => Dispatch(() =>
        {
            library.ApplyProfiles(state);
            restorer?.Select(state, library.SavedDeviceSession, library.DeviceProfileKey);
            library.RetryProfileRestore = restorer is null ? null : restorer.Retry;
            network.ApplyActiveProfile(state.ActiveStorage == BrowserProfileStorageLocation.Television,
                state.ActiveProfileId, state.Epoch);
            FlintDiag.Info(
                "FlintBrowser",
                $"profiles snapshot count={state.TvProfiles.Count} storage={state.ActiveStorage}");
        });
        void OnNetwork(BrowserNetworkSnapshot state) => Dispatch(() =>
        {
            network.Apply(state);
            FlintDiag.Info(
                "FlintVpn",
                $"network snapshot profile={state.ProfileId} enabled={state.VpnEnabled} session={state.SessionState} preparable={state.CapabilityPreparable}");
        });
        void OnWorkspace(BrowserWorkspaceSnapshot state) => Dispatch(() =>
        {
            workspace.ApplySnapshot(state);
            restorer?.Observe(state);
            if (restorer?.CanCapture == true) library.CaptureWorkspace(state);
            FlintDiag.Info(
                "FlintWorkspace",
                $"workspace epoch={state.Epoch} rev={state.Revision} layout={state.Layout} panes={state.Panes.Count} focused={state.FocusedPaneId ?? "(none)"}");
        });

        cockpit.TabsReceived += OnTabs;
        cockpit.ViewReceived += OnView;
        cockpit.LibraryReceived += OnLibrary;
        cockpit.DeviceLibraryRequestReceived += OnDeviceRequest;
        cockpit.ProfilesReceived += OnProfiles;
        cockpit.NetworkReceived += OnNetwork;
        cockpit.WorkspaceReceived += OnWorkspace;
        unsubscribe = () =>
        {
            cockpit.TabsReceived -= OnTabs;
            cockpit.ViewReceived -= OnView;
            cockpit.LibraryReceived -= OnLibrary;
            cockpit.DeviceLibraryRequestReceived -= OnDeviceRequest;
            cockpit.ProfilesReceived -= OnProfiles;
            cockpit.NetworkReceived -= OnNetwork;
            cockpit.WorkspaceReceived -= OnWorkspace;
        };
    }

    public void Detach()
    {
        generation++;
        restorer?.Dispose();
        restorer = null;
        unsubscribe?.Invoke();
        unsubscribe = null;
        remote = null;
        appliedFeatures = BrowserCockpitFeatures.None;
        tabs.Bind(null);
        tabs.Reset(false);
        viewControls.Bind(null);
        viewControls.Reset(false);
        library.Bind(null);
        library.Reset(false, false);
        network.Bind(null);
        workspace.Bind(null);
        workspace.Reset();
    }

    private void ApplyFeatures(BrowserCockpitFeatures features)
    {
        // Discovering another family must not erase earlier snapshots or the user's drafts.
        var added = features & ~appliedFeatures;
        if (added.HasFlag(BrowserCockpitFeatures.Tabs)) tabs.Reset(true);
        if (added.HasFlag(BrowserCockpitFeatures.View)) viewControls.Reset(true);
        library.IsAvailable = features.HasFlag(BrowserCockpitFeatures.Library);
        library.IsProfileSelectionAvailable = features.HasFlag(BrowserCockpitFeatures.Profiles);
        if (added.HasFlag(BrowserCockpitFeatures.Network)) network.Reset(true);
        if (added.HasFlag(BrowserCockpitFeatures.Workspace)) workspace.Bind(remote?.WorkspaceCommandSink);
        appliedFeatures = features;
    }
}
