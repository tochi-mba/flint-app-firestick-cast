using Flint.App.Services;
using Flint.Protocol;
using Flint.Session.Browser;

namespace Flint.App.ViewModels;

/// <summary>
/// Owns the life of the pinned TLS session: opening it, replacing it, watching it end.
/// </summary>
/// <remarks>
/// The page's hardest bugs have all lived here, and they share a shape — the page believing in a
/// session that is gone. A SecureReady phase over a dead transport left GO disabled with Verify
/// hidden and nothing to click; a command counter reset on reconnect made the first tab click
/// STALE_COMMAND. Both were fixed by being exact about what survives a transport and what does not,
/// so that is what this class is for.
///
/// It takes the page rather than an interface: every field it touches belongs to the page, and a
/// seam wide enough to cover them would be a copy of the view model rather than an abstraction
/// over it.
/// </remarks>
internal sealed class BrowserSecureSessionController(BrowserPageViewModel page)
{
    private long watchGeneration;

    /// <summary>
    /// Opens the pinned session.
    /// </summary>
    /// <param name="allowFirstUseAccept">
    /// True only for a Verify click, where a person has just compared the two codes. An unattended
    /// reconnect passes false and takes a mismatch rather than pinning something new.
    /// </param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    public async Task ConnectAsync(bool allowFirstUseAccept, CancellationToken cancellationToken)
    {
        page.SessionPhase = BrowserUiPhase.Verifying;
        page.LastError = null;
        page.FingerprintDisplay = null;
        page.RaiseSessionSurfaceProperties();
        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"verify begin firstUseAccept={allowFirstUseAccept} rememberedPin={page.HasRememberedTrust} pairingCodePresent={page.HasUsablePairingCode}");

        if (page.ResolveEndpoint() is not { } endpoint)
        {
            Fail("Secure verification needs the TV's browser port. Type the port shown under BROWSER PORT on the receiver, or run discovery while the receiver is advertising.");
            return;
        }

        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"verify endpoint={endpoint.Address}:{endpoint.Port} identity={endpoint.ReceiverIdentity}");

        var pairingCode = page.PairingCode;
        if (pairingCode.Length != 6 || !pairingCode.All(char.IsDigit))
        {
            Fail("Enter the six-digit pairing code from Cast before verifying the secure browser session.");
            return;
        }

        try
        {
            IBrowserTrustPrompter prompter = allowFirstUseAccept
                ? new VerifyClickTrustPrompter(code => page.FingerprintDisplay = code)
                : new RejectFirstUseTrustPrompter();
            var next = await page.Connector
                .ConnectAsync(endpoint, pairingCode, page.TrustStore, prompter, cancellationToken)
                .ConfigureAwait(true);
            await ReplaceAsync(next).ConfigureAwait(true);
            page.FingerprintDisplay = next.PeerIdentity.Fingerprint.DisplayCode;
            page.SessionPhase = BrowserUiPhase.SecureReady;
            page.LastError = null;
            page.ApplyRememberedTrust(true);
            page.RaiseVerdictChanged();
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"verify ok phase=SecureReady displayCode={page.FingerprintDisplay}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Fail("Secure verification was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            Fail(BrowserVerdictCopy.SafeMessage(exception));
        }
        finally
        {
            page.RaiseSessionSurfaceProperties();
        }
    }

    /// <summary>
    /// Swaps in a new transport, keeping exactly what the receiver keeps.
    /// </summary>
    /// <remarks>
    /// The epoch and the command counter survive: the receiver remembers both across a dead TLS
    /// session, and resetting the counter made reconnected tab clicks fail as STALE_COMMAND. The
    /// input sequence does not survive, because that one is per-session on the receiver.
    /// </remarks>
    public async Task ReplaceAsync(ISecureBrowserRemote next)
    {
        if (page.Session is { } previous)
        {
            previous.StateReceived -= page.HandleState;
            await previous.DisposeAsync().ConfigureAwait(true);
        }

        page.AdoptSession(next);
        // The cockpit rides the same session and borrows its identifiers, so it is rebuilt with the
        // transport rather than outliving the one it writes to.
        page.AttachCockpit(next.CreateCockpit(() => page.Epoch, () => page.NextCommandId++));
        page.ResetInputSequence();
        page.SurfaceOpen = false;
        page.ActiveNavigationId = 0;
        page.CurrentUrl = null;
        page.IsLoading = false;
        // The receiver's own capability answer decides whether preview can be offered at all. A
        // toggle on a receiver that cannot capture would be a control that does nothing.
        page.Preview.Configure(next.Capability.PreviewSupported);
        page.ResetPreviewSync();
        page.TogglePreviewCommand.NotifyCanExecuteChanged();
        next.StateReceived += page.HandleState;
        next.PreviewReceived += page.HandlePreview;
        next.DialogReceived += page.HandleDialog;
        Watch(next);
        page.RaiseTouchAvailability();
        page.RaiseSessionSurfaceProperties();
    }

    /// <summary>
    /// Returns the page to Idle when the receive loop ends.
    /// </summary>
    /// <remarks>
    /// Without this, a receiver restart left the page in SecureReady over a dead transport: GO
    /// disabled, Verify hidden, and no path back except restarting the app. The generation check
    /// keeps a stale transport's ending from tearing down its replacement.
    /// </remarks>
    private void Watch(ISecureBrowserRemote remote)
    {
        var generation = ++watchGeneration;
        _ = remote.Completion.ContinueWith(
            _ => page.Dispatcher.Dispatch(() =>
            {
                if (page.IsDisposed
                    || generation != watchGeneration
                    || !ReferenceEquals(page.Session, remote))
                {
                    return;
                }

                MarkLost("transport ended");
                page.RequestReconnect();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Clears a dead session and returns the page to an honest Idle phase.</summary>
    public void MarkLost(string reason)
    {
        Flint.Core.FlintDiag.Warn("FlintBrowser", $"secure session lost: {reason}");
        watchGeneration++;
        if (page.Session is { } ending)
        {
            ending.StateReceived -= page.HandleState;
            ending.PreviewReceived -= page.HandlePreview;
            ending.DialogReceived -= page.HandleDialog;
            page.AdoptSession(null);
            _ = ending.DisposeAsync().AsTask();
        }

        page.DetachCockpit();
        page.SurfaceOpen = false;
        page.ActiveNavigationId = 0;
        page.IsLoading = false;
        page.PageProgress = null;
        if (page.SessionPhase is BrowserUiPhase.SecureReady or BrowserUiPhase.Verifying)
        {
            page.SessionPhase = BrowserUiPhase.Idle;
        }

        page.RaiseTouchAvailability();
        page.RaiseSessionSurfaceProperties();
        page.RaiseVerdictChanged();
    }

    /// <summary>
    /// Closes the browser surface on the television, leaving the session intact.
    /// </summary>
    /// <remarks>
    /// The strip is captured before the close, not after: the receiver publishes a single-tab
    /// snapshot on the way down, and reading the strip afterwards would record that instead of what
    /// the person actually had open.
    /// </remarks>
    public async Task StopSurfaceAsync(CancellationToken cancellationToken)
    {
        if (!page.SurfaceOpen || page.Session is not { IsConnected: true } session)
        {
            page.SurfaceOpen = false;
            page.ActiveNavigationId = 0;
            page.RaiseSurfaceOpenChanged();
            return;
        }

        try
        {
            var commandId = page.NextCommandId++;
            await session.SendCommandAsync(
                    new BrowserCommandMessage(page.Epoch, commandId, BrowserCommandAction.Close),
                    cancellationToken)
                .ConfigureAwait(true);
            page.LastError = null;
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"command Close epoch={page.Epoch} cmdId={commandId}");
        }
        catch (Exception exception)
            when (exception is IOException or ArgumentException or OperationCanceledException)
        {
            page.LastError = BrowserVerdictCopy.SafeMessage(exception);
            Flint.Core.FlintDiag.Warn("FlintBrowser", $"command Close failed: {exception.GetType().Name}");
        }
        finally
        {
            page.CaptureTabSession();
            page.SurfaceOpen = false;
            page.ActiveNavigationId = 0;
            page.PageTitle = null;
            page.PageProgress = null;
            page.CurrentUrl = null;
            page.IsLoading = false;
            page.RaiseTouchAvailability();
            page.RaiseSurfaceOpenChanged();
        }
    }

    /// <summary>
    /// Records a verification refusal, leaving the page on a mismatch a person can read.
    /// </summary>
    /// <remarks>
    /// Drops the transport as well as the phase. A half-open session behind a mismatch is what the
    /// next reconnect pass would mistake for a working one.
    /// </remarks>
    private void Fail(string message)
    {
        page.SessionPhase = BrowserUiPhase.Mismatch;
        page.LastError = message;
        Flint.Core.FlintDiag.Warn("FlintBrowser", $"verify mismatch: {message}");
        if (page.Session is { } ending)
        {
            ending.StateReceived -= page.HandleState;
            _ = ending.DisposeAsync().AsTask();
            page.AdoptSession(null);
        }
    }
}
