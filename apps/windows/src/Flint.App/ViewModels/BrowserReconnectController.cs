using Flint.App.Services;
using Flint.Session.Browser;

namespace Flint.App.ViewModels;

/// <summary>
/// Treats the Verify click as the first-use accept, after the user has compared the two codes.
/// </summary>
internal sealed class VerifyClickTrustPrompter(Action<string> onFingerprint) : IBrowserTrustPrompter
{
    public ValueTask<bool> ConfirmFirstUseAsync(
        BrowserPeerIdentity identity,
        CancellationToken cancellationToken = default)
    {
        onFingerprint(identity.Fingerprint.DisplayCode);
        return ValueTask.FromResult(true);
    }
}

/// <summary>
/// Refuses first use.
/// </summary>
/// <remarks>
/// A silent reconnect must never invent a pin. If the certificate does not match the store, the
/// only honest outcome is a mismatch the user can see — trusting it on first sight would make an
/// unattended path do the one thing that requires a person.
/// </remarks>
internal sealed class RejectFirstUseTrustPrompter : IBrowserTrustPrompter
{
    public ValueTask<bool> ConfirmFirstUseAsync(
        BrowserPeerIdentity identity,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(false);
}

/// <summary>
/// Reconnects a receiver that was verified before, without asking anyone anything.
/// </summary>
/// <remarks>
/// Requests arrive from several directions at once — discovery reports, a pairing code being typed,
/// a port being edited, a transport ending — so they are coalesced rather than run in parallel. A
/// pass that is already running absorbs a new request instead of starting a second connect against
/// the same receiver.
///
/// The single deferral in <see cref="DrainAsync"/> is load-bearing: without it, the synchronous
/// property notifications raised during a pass can request another before the shared task has been
/// assigned, and that request is then dropped.
/// </remarks>
internal sealed class BrowserReconnectController(BrowserPageViewModel page)
{
    private Task work = Task.CompletedTask;
    private bool requested;

    /// <summary>Requests a pass, joining one already in flight.</summary>
    public Task RequestAsync()
    {
        requested = true;
        if (!work.IsCompleted)
        {
            return work;
        }

        work = DrainAsync();
        return work;
    }

    private async Task DrainAsync()
    {
        await Task.Yield();
        while (requested && !page.IsDisposed)
        {
            requested = false;
            await RunAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// One pass: refresh what is remembered, then reconnect if nothing is missing.
    /// </summary>
    /// <remarks>
    /// Swallows everything. Reconnect is opportunistic and runs off ordinary property churn, so a
    /// failure here must not surface as an unhandled fault on an unrelated page.
    /// </remarks>
    private async Task RunAsync()
    {
        try
        {
            var remembered = await page.TrustStore
                .FindAsync(BrowserEndpointResolver.DefaultReceiverIdentity)
                .ConfigureAwait(true);
            page.ApplyRememberedTrust(remembered is not null);

            // A SecureReady phase over a dead TLS session — a receiver restart or reinstall — has
            // to fall back to Idle first, or CanNavigate and CanVerify both stay false and the page
            // is stuck with nothing to click.
            if (page.SessionPhase is BrowserUiPhase.SecureReady && !page.HasLiveSession)
            {
                page.MarkSessionLost("session not connected during reconnect pass");
            }

            if (!page.HasRememberedTrust
                || page.IsDisposed
                || page.SessionPhase is not BrowserUiPhase.Idle
                || !page.CanVerify)
            {
                if (page.HasRememberedTrust && !page.IsDisposed && page.SessionPhase is BrowserUiPhase.Idle)
                {
                    Flint.Core.FlintDiag.Info(
                        "FlintBrowser",
                        $"auto-reconnect deferred canVerify={page.CanVerify}");
                }

                return;
            }

            Flint.Core.FlintDiag.Info("FlintBrowser", "auto-reconnect begin");
            page.SetReconnecting(true);
            try
            {
                await page.ConnectAsync(allowFirstUseAccept: false, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            finally
            {
                page.SetReconnecting(false);
            }
        }
        catch
        {
            // See the remarks: property churn must not become an unhandled fault.
        }
    }
}
