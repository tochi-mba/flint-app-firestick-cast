using Flint.App.Services;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>
/// Turns what the television reports into what the page shows.
/// </summary>
/// <remarks>
/// Every field this writes is owned by the page, so it takes the page rather than an interface: a
/// fourteen-member seam would be a description of the view model, not an abstraction over it.
///
/// Two authorities report the active page and they do not always agree. A
/// <see cref="BrowserStateMessage"/> describes the surface; a tab snapshot describes the strip, and
/// arrives when a state message was missed or when Navigate never produced one. Both land here so
/// the reconciliation is in one place — the HUD stuck at a mid-load percentage on a page the
/// television had already finished was the two of them being applied in different ways.
///
/// Everything here runs on the UI thread. Avalonia bindings do not reliably refresh when
/// CommunityToolkit raises PropertyChanged from the TLS receive loop.
/// </remarks>
internal sealed class BrowserReceiverStateApplier(BrowserPageViewModel page)
{
    private long lastLoggedNavigationId = -1;
    private BrowserLoadState? lastLoggedLoadState;
    private string? lastLoggedHost;

    /// <summary>Projects a television page snapshot onto the HUD.</summary>
    public void Apply(BrowserStateMessage state)
    {
        AdoptEpoch(state.Epoch);
        AdoptCommandWatermark(state.LastAcceptedCommandId);
        if (state.NavigationId > 0)
        {
            page.ActiveNavigationId = state.NavigationId;
        }

        if (state.LoadState is not BrowserLoadState.Idle and not BrowserLoadState.Closed)
        {
            page.SurfaceOpen = true;
        }

        page.PageTitle = string.IsNullOrWhiteSpace(state.Title) ? null : state.Title;
        page.CanGoBack = state.CanGoBack;
        page.CanGoForward = state.CanGoForward;
        ApplyLoadPresentation(state.LoadState == BrowserLoadState.Loading, state.Progress);
        page.CurrentUrl = string.IsNullOrWhiteSpace(state.Url) ? null : state.Url;
        _ = page.Library.ObservePageAsync(state);
        // The receiver is the authority on whether preview is actually running. Trusting the local
        // request instead would leave the panel claiming "live" through a capture that stopped.
        page.Preview.ApplyState(state.PreviewState);
        page.RaiseTouchAvailability();
        page.SyncPreviewAfterAdoptedSurface(state);

        LogStateChange(state);

        // The receiver reports what it is actually showing, including its own failures. Surfacing
        // that here is what keeps the desktop honest about a page that did not load, rather than
        // leaving the address the user typed on screen as though it had worked.
        if (state.LoadState == BrowserLoadState.Failed && state.ErrorDetail is { Length: > 0 } detail)
        {
            page.LastError = detail;
            Flint.Core.FlintDiag.Warn("FlintBrowser", $"page load failed reasonLen={detail.Length}");
        }
    }

    /// <summary>
    /// Projects the tab strip's view of the active page onto the HUD.
    /// </summary>
    /// <remarks>
    /// The second authority. When tabs say the foreground page is ready, the HUD has to clear
    /// Loading even if no state message said so.
    /// </remarks>
    public void ApplyActiveTab(BrowserTabsSnapshot snapshot)
    {
        // Orphan tabs after a reconnect publish the television's live epoch before Windows has
        // opened anything. A tab command sent with epoch 0 throws WireFormatException, which used
        // to take the whole app down rather than fail one click.
        AdoptEpoch(snapshot.Epoch);
        if (snapshot.Tabs.Count > 0)
        {
            page.SurfaceOpen = true;
        }

        var active = snapshot.Tabs.FirstOrDefault(tab => tab.Id == snapshot.ActiveTabId)
            ?? snapshot.Tabs.FirstOrDefault();
        if (active is null)
        {
            return;
        }

        ApplyLoadPresentation(active.IsLoading, active.Progress);
        if (!string.IsNullOrWhiteSpace(active.Title))
        {
            page.PageTitle = active.Title;
        }

        if (!string.IsNullOrWhiteSpace(active.Url))
        {
            page.CurrentUrl = active.Url;
        }

        page.CanGoBack = active.CanGoBack;
        page.CanGoForward = active.CanGoForward;
    }

    /// <summary>Aligns the host command epoch with what the television already has open.</summary>
    public void AdoptEpoch(long epoch)
    {
        if (epoch <= 0 || epoch <= page.Epoch)
        {
            return;
        }

        page.Epoch = epoch;
        Flint.Core.FlintDiag.Info("FlintBrowser", $"adopted receiver epoch={epoch}");
    }

    /// <summary>Advances the host command counter past what the television has already accepted.</summary>
    public void AdoptCommandWatermark(long lastAcceptedCommandId)
    {
        if (lastAcceptedCommandId < page.NextCommandId)
        {
            return;
        }

        page.NextCommandId = lastAcceptedCommandId + 1;
        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"adopted command watermark lastAccepted={lastAcceptedCommandId} next={page.NextCommandId}");
    }

    /// <summary>Sets the loading flag and the percentage the status line shows.</summary>
    public void ApplyLoadPresentation(bool loading, int progress)
    {
        var bounded = Math.Clamp(progress, 0, 100);
        page.IsLoading = loading;
        // Ready pages must not keep a mid-load percent in the status line. WebView often finishes
        // before onProgressChanged reaches 100; the television already shows ready in that case.
        page.PageProgress = loading ? bounded : bounded > 0 ? 100 : null;
    }

    /// <summary>
    /// One log line per real change, rather than one per state message.
    /// </summary>
    /// <remarks>
    /// A loading page emits a state message per progress tick. Logging each would bury the
    /// navigation and failure lines that are actually worth reading.
    /// </remarks>
    private void LogStateChange(BrowserStateMessage state)
    {
        var host = Flint.Core.FlintDiag.SafeHost(state.Url);
        if (state.NavigationId == lastLoggedNavigationId
            && state.LoadState == lastLoggedLoadState
            && string.Equals(host, lastLoggedHost, StringComparison.Ordinal)
            && state.LoadState != BrowserLoadState.Failed)
        {
            return;
        }

        lastLoggedNavigationId = state.NavigationId;
        lastLoggedLoadState = state.LoadState;
        lastLoggedHost = host;
        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"state navId={state.NavigationId} load={state.LoadState} progress={state.Progress} host={host} preview={state.PreviewState}");
    }
}
