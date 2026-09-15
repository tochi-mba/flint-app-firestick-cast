using Flint.App.Services;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>
/// The commands that move the page: opening an address, and walking its history.
/// </summary>
/// <remarks>
/// Both paths have to take the television's glass back first — Mirror or Media may own it — and
/// both have to be honest about what a successful send does and does not mean. A wire send is not
/// an acknowledgement: flipping the HUD to Loading here left it stuck on "Loading… N%" over a page
/// the television had already finished. Load state comes from the receiver alone.
/// </remarks>
internal sealed class BrowserNavigator(BrowserPageViewModel page)
{
    /// <summary>
    /// Opens the resolved address, as Enter and GO both do.
    /// </summary>
    /// <remarks>
    /// Open and Navigate are different commands and the surface decides which. A fresh Open after a
    /// close or a reconnect also has to beat the receiver's remembered epoch, or the receiver
    /// discards it as stale.
    /// </remarks>
    public async Task NavigateAsync(CancellationToken cancellationToken)
    {
        if (page.Session is not { IsConnected: true } session)
        {
            return;
        }

        var url = BrowserAddressBarResolver.TryResolve(page.Address);
        if (url is null)
        {
            page.LastError = "Type a site or a Google search, then press Enter.";
            return;
        }

        page.Address = url;

        try
        {
            // Always yield the glass. Navigating while Screen owned it used to be skipped when a
            // stale surface-open flag was still true.
            await page.PrepareBrowserGlassAsync(cancellationToken).ConfigureAwait(true);

            var commandId = page.NextCommandId++;
            BrowserCommandAction action;
            if (page.SurfaceOpen)
            {
                action = BrowserCommandAction.Navigate;
            }
            else
            {
                page.Epoch = Math.Max(page.Epoch + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                action = BrowserCommandAction.Open;
            }

            await session.SendCommandAsync(
                    new BrowserCommandMessage(page.Epoch, commandId, action, Url: url),
                    cancellationToken)
                .ConfigureAwait(true);
            page.SurfaceOpen = true;
            page.ActiveNavigationId = commandId;
            // Deliberately not touching IsLoading — see the class remarks.
            page.CurrentUrl = url;
            page.LastError = null;
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"command {action} epoch={page.Epoch} cmdId={commandId} host={Flint.Core.FlintDiag.SafeHost(url)}");
            page.RaiseTouchAvailability();
            page.RaiseSurfaceOpenChanged();
            await page.SyncPreviewPreferenceAsync(cancellationToken).ConfigureAwait(true);
            page.MarkPreviewSynced();
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            page.LastError = BrowserVerdictCopy.SafeMessage(exception);
            Flint.Core.FlintDiag.Warn(
                "FlintBrowser",
                $"command navigate/open failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Sends a command that does not depend on a page being open.
    /// </summary>
    /// <remarks>
    /// Clearing browsing data is the case this exists for. The receiver deliberately exempts it
    /// from both its own gates — the VPN policy and the workspace check — because erasing data must
    /// work whenever the session does. Routing it through the ordinary path meant the host dropped
    /// it whenever no page happened to be open, so a person could confirm a destructive dialog and
    /// have nothing happen, with nothing said.
    ///
    /// It also does not take the television's glass. Clearing data is not a reason to interrupt
    /// whatever is on screen.
    /// </remarks>
    public async Task SendSessionActionAsync(
        BrowserCommandAction action,
        CancellationToken cancellationToken)
    {
        if (page.Session is not { IsConnected: true } session)
        {
            return;
        }

        try
        {
            var commandId = page.NextCommandId++;
            await session.SendCommandAsync(
                    new BrowserCommandMessage(page.Epoch, commandId, action),
                    cancellationToken)
                .ConfigureAwait(true);
            page.LastError = null;
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"session command {action} epoch={page.Epoch} cmdId={commandId}");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            page.LastError = BrowserVerdictCopy.SafeMessage(exception);
            Flint.Core.FlintDiag.Warn(
                "FlintBrowser",
                $"session command {action} failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Sends one command that carries no address — back, forward, reload, stop, clear data.
    /// </summary>
    /// <remarks>
    /// Taking the glass can itself close the surface, so the check happens after the prepare rather
    /// than before it. Sending into a surface that is no longer there is silently dropped by the
    /// receiver, which is far harder to diagnose than a logged skip.
    /// </remarks>
    public async Task SendActionAsync(BrowserCommandAction action, CancellationToken cancellationToken)
    {
        if (page.Session is not { IsConnected: true } session)
        {
            return;
        }

        try
        {
            await page.PrepareBrowserGlassAsync(cancellationToken).ConfigureAwait(true);

            if (!page.SurfaceOpen)
            {
                Flint.Core.FlintDiag.Info(
                    "FlintBrowser",
                    $"command {action} skipped — browser surface closed after prepare");
                return;
            }

            var commandId = page.NextCommandId++;
            await session.SendCommandAsync(
                    new BrowserCommandMessage(page.Epoch, commandId, action),
                    cancellationToken)
                .ConfigureAwait(true);
            page.LastError = null;
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"command {action} epoch={page.Epoch} cmdId={commandId}");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            page.LastError = BrowserVerdictCopy.SafeMessage(exception);
            Flint.Core.FlintDiag.Warn(
                "FlintBrowser",
                $"command {action} failed: {exception.GetType().Name}");
        }
    }
}
