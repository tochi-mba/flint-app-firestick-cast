using Flint.App.Services;

namespace Flint.App.ViewModels;

/// <summary>What restoring a closed tab session needs from the page.</summary>
internal interface IBrowserTabSessionHost
{
    /// <summary>Whether the television is still showing the browser.</summary>
    bool SurfaceOpen { get; }

    /// <summary>The Windows-held tab strip for the current profile session.</summary>
    BrowserTabsViewModel Tabs { get; }

    /// <summary>Takes the television's glass for the browser, stopping mirror or media first.</summary>
    Task PrepareBrowserGlassAsync(CancellationToken cancellationToken);

    /// <summary>Opens one address on the television, as the address bar would.</summary>
    Task OpenAddressAsync(string url, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps a profile's tab session alive across a television surface that was taken away.
/// </summary>
/// <remarks>
/// Bookmarks and history persist on disk, but the live strip belongs to the Windows session. When
/// Mirror or Media claims the glass, the receiver closes the browser and destroys every tab id it
/// held — so a chip clicked afterwards refers to a tab that no longer exists on the television.
///
/// Clicking that chip has to reopen the whole session, not just the one URL, or a five-tab strip
/// silently collapses to one. That is the entire reason this class exists, and why the URLs are
/// captured *before* the surface goes away rather than read back from a receiver that has forgotten
/// them.
/// </remarks>
internal sealed class BrowserTabSessionRestorer(IBrowserTabSessionHost host)
{
    private const string StartPage = "https://www.google.com/";

    private (long Id, string Url)[] suspended = [];

    /// <summary>How many tabs are being held for a surface that is currently closed.</summary>
    internal int SuspendedCount => suspended.Length;

    /// <summary>
    /// Remembers the strip before the surface is given up.
    /// </summary>
    /// <remarks>
    /// Read from the Windows-held list, because by the time the surface is gone the receiver's own
    /// tab state is already empty.
    /// </remarks>
    public void Capture()
    {
        suspended = LiveTabs();
        if (suspended.Length == 0)
        {
            return;
        }

        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"suspend session tabs={suspended.Length} refs=[{Refs(suspended)}]");
    }

    /// <summary>
    /// Reclaims the glass before a tab chip is acted on.
    /// </summary>
    /// <returns>
    /// True to send the tab wire command; false when this already reopened the page itself, which
    /// is required after the surface closed and the receiver's tab ids went with it.
    /// </returns>
    public async Task<bool> BeforeTabCommandAsync(
        BrowserTabRequest request,
        CancellationToken cancellationToken)
    {
        // Never prepare and then navigate: both take the coordinator gate, and SemaphoreSlim is not
        // re-entrant, so the pair deadlocks on the UI thread.
        if (host.SurfaceOpen)
        {
            await host.PrepareBrowserGlassAsync(cancellationToken).ConfigureAwait(true);
            return true;
        }

        if (request.Operation is BrowserTabOperation.Select or BrowserTabOperation.Duplicate
            && (suspended.Length > 0 || host.Tabs.Items.Count > 0))
        {
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"tab {request.Operation} restores closed profile session tabs={Math.Max(suspended.Length, host.Tabs.Items.Count)} focusId={request.TabId}");
            await RestoreAsync(request.TabId, cancellationToken).ConfigureAwait(true);
            return false;
        }

        if (request.Operation == BrowserTabOperation.New)
        {
            // New cannot target a destroyed tab id, so it becomes an ordinary open.
            Flint.Core.FlintDiag.Info("FlintBrowser", "tab New resumes closed surface with start page");
            await host.OpenAddressAsync(StartPage, cancellationToken).ConfigureAwait(true);
            return false;
        }

        await host.PrepareBrowserGlassAsync(cancellationToken).ConfigureAwait(true);
        return true;
    }

    /// <summary>Rebuilds the strip from the held URLs, focusing the tab that was clicked.</summary>
    private async Task RestoreAsync(long preferredTabId, CancellationToken cancellationToken)
    {
        var tabs = suspended.Length > 0 ? suspended : LiveTabs();
        if (tabs.Length == 0)
        {
            await host.OpenAddressAsync(StartPage, cancellationToken).ConfigureAwait(true);
            return;
        }

        var focus = Array.FindIndex(tabs, tab => tab.Id == preferredTabId);
        if (focus < 0)
        {
            focus = 0;
        }

        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"restore session focus={Flint.Core.FlintDiag.SafeTabRef(tabs[focus].Url)} siblings={tabs.Length - 1} refs=[{Refs(tabs)}]");

        // The focused tab reopens the surface; the rest are added to it, so this one goes first.
        await host.OpenAddressAsync(tabs[focus].Url, cancellationToken).ConfigureAwait(true);

        for (var index = 0; index < tabs.Length; index++)
        {
            if (index == focus)
            {
                continue;
            }

            var sibling = tabs[index].Url;
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"restore sibling New {Flint.Core.FlintDiag.SafeTabRef(sibling)}");
            await host.Tabs.OpenUrlAsync(sibling, cancellationToken).ConfigureAwait(true);
        }

        suspended = [];
    }

    private (long Id, string Url)[] LiveTabs() => host.Tabs.Items
        .Where(tab => !string.IsNullOrWhiteSpace(tab.Url))
        .Select(tab => (tab.Id, Url: tab.Url))
        .ToArray();

    private static string Refs((long Id, string Url)[] tabs) =>
        string.Join(", ", tabs.Select(tab => Flint.Core.FlintDiag.SafeTabRef(tab.Url)));
}
