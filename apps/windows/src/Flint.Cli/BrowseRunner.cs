using System.Net;
using Flint.App.Services;
using Flint.Protocol;
using Flint.Session.Browser;

namespace Flint.Cli;

/// <summary>
/// Drives a Fire TV-resident browser from the command line.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the browser can be exercised end to end without driving the desktop UI by hand. A
/// scripted run against a real television proves more than any number of fakes, and it needs
/// something to run.
/// </para>
/// <para>
/// The browser has its own separately authenticated, certificate-pinned session — it does not share
/// the cast channel — so this connects independently of any mirroring session and never borrows the
/// cast session's trust.
/// </para>
/// </remarks>
internal static class BrowseRunner
{
    /// <summary>
    /// Opens a page on the television and holds the session until cancelled.
    /// </summary>
    /// <returns>A process exit code: zero when the page was opened.</returns>
    internal static async Task<int> RunAsync(
        IPAddress address,
        int browserPort,
        string pairingCode,
        string url,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var endpoint = new BrowserEndpoint(address, browserPort, address.ToString());
        // The same store the desktop app uses, not a fresh one per run. A pin that evaporates when
        // the process exits means the person re-verifies the same television every single time,
        // which trains them to accept the prompt without reading it — the one thing certificate
        // pinning exists to prevent.
        var trustStore = BrowserTrustStoreLocation.Open();
        var prompter = new ConsoleTrustPrompter();

        Console.WriteLine();
        Console.WriteLine($"  Browser session: {endpoint.Address}:{endpoint.Port}");

        await using var session = await BrowserSession.ConnectAsync(
                endpoint,
                pairingCode,
                trustStore,
                prompter,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"  Pinned to {session.PeerIdentity.Fingerprint.DisplayCode}");

        // Printed as it arrives, so a scripted run leaves a transcript of what the page did rather
        // than only whether the command was accepted.
        session.StateReceived += state =>
        {
            var title = string.IsNullOrWhiteSpace(state.Title) ? "(no title)" : state.Title;
            Console.WriteLine($"  page: {state.LoadState} {state.Progress}% {title}");
        };

        // The receiver remembers the highest epoch it has seen and refuses anything at or below it,
        // deliberately, so a command replayed from a dead session cannot reopen a surface. A
        // hardcoded epoch therefore works exactly once per receiver launch and is refused for the
        // rest of it — which is what a real Fire TV reported as STALE_EPOCH. Wall-clock
        // milliseconds always advance, which is the same rule the Windows app follows.
        var epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await session.SendCommandAsync(
                new BrowserCommandMessage(epoch, 1, BrowserCommandAction.Open, Url: url),
                cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine($"  Opened {url}. Ctrl+C to close the surface.");

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C is how this ends; closing the surface on the way out is the courtesy that stops
            // the television sitting on a page nobody is driving.
            await CloseQuietlyAsync(session, epoch).ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>Asks the receiver to leave the browser surface, ignoring a dead session.</summary>
    private static async Task CloseQuietlyAsync(BrowserSession session, long epoch)
    {
        try
        {
            using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await session.SendCommandAsync(
                    new BrowserCommandMessage(epoch, 2, BrowserCommandAction.Close),
                    closing.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException)
        {
            // The session is already gone, which is the common case when the television dropped it.
        }
    }

    /// <summary>
    /// Accepts a receiver's certificate on first use, after printing it.
    /// </summary>
    /// <remarks>
    /// The desktop app asks a person to compare the code on the television with the code on screen.
    /// A command-line run has nobody to ask, so it prints the fingerprint and continues — which is
    /// trust on first use with the comparison left to whoever reads the output.
    ///
    /// That is weaker than the app's flow and is stated plainly rather than hidden: this runner is a
    /// diagnostic, and the pin it accepts lives only for the life of the process.
    /// </remarks>
    private sealed class ConsoleTrustPrompter : IBrowserTrustPrompter
    {
        public ValueTask<bool> ConfirmFirstUseAsync(
            BrowserPeerIdentity identity,
            CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"  First use. Receiver fingerprint: {identity.Fingerprint.DisplayCode}");
            Console.WriteLine("  Compare it with the code on the television before trusting this run.");
            return ValueTask.FromResult(true);
        }
    }
}
