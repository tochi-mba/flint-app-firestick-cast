using Flint.App.Services;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>What sending input needs to know about the page it is sending from.</summary>
/// <remarks>
/// A narrow seam rather than a reference back to the whole page: everything here is read at the
/// moment of a send, and nothing the controller does can reach further into the view model than
/// these five members.
/// </remarks>
internal interface IBrowserInputHost
{
    /// <summary>The pinned session, or null when there is nothing to send over.</summary>
    ISecureBrowserRemote? Session { get; }

    /// <summary>The session epoch every input is stamped with.</summary>
    long Epoch { get; }

    /// <summary>The navigation the television is currently showing.</summary>
    long ActiveNavigationId { get; }

    /// <summary>Whether pointer and scroll input would reach a live page.</summary>
    bool CanSendTouch { get; }

    /// <summary>The preview, which supplies the frame a pointer sample is measured against.</summary>
    BrowserPreviewViewModel Preview { get; }

    /// <summary>Reports a send failure, or clears the last one when passed null.</summary>
    void ReportInputResult(string? failure);
}

/// <summary>
/// Numbers, sends and records every input the desktop puts onto the television.
/// </summary>
/// <remarks>
/// Split out of the page because it is the one part of it with a running invariant: the receiver
/// accepts inputs by increasing sequence and drops anything it has already seen, so the counter and
/// the sending have to stay together. Holding them next to twenty unrelated bound properties made
/// that easy to miss.
///
/// Diagnostics live here for the same reason — the breadcrumbs are keyed to the sequence number.
/// </remarks>
internal sealed class BrowserInputController(IBrowserInputHost host)
{
    private long nextSequence = 1;
    private BrowserPointerAction? lastLoggedPointerAction;
    private long lastLoggedSequence = -1;

    /// <summary>The next sequence number, exposed so the page's tests can assert numbering.</summary>
    internal long NextSequence => nextSequence;

    /// <summary>
    /// Restarts numbering for a replacement transport.
    /// </summary>
    /// <remarks>
    /// Input sequence is per-session on the receiver, unlike the command counter, which it
    /// remembers across a dead TLS session. Resetting the wrong one of the two made the first tab
    /// clicks after a reconnect fail as STALE_COMMAND.
    /// </remarks>
    public void Reset() => nextSequence = 1;

    /// <summary>Sends a pointer sample, resolving refs from the live preview when not given.</summary>
    public Task SendPointerAsync(
        BrowserPointerAction action,
        int x,
        int y,
        int buttons,
        long navigationId,
        long frameId,
        CancellationToken cancellationToken)
    {
        if (!host.CanSendTouch)
        {
            return Task.CompletedTask;
        }

        var (nav, frame) = ResolveRefs(navigationId, frameId);
        if (nav <= 0 || frame <= 0)
        {
            return Task.CompletedTask;
        }

        return SendAsync(new BrowserPointerInput(action, nav, frame, x, y, buttons), cancellationToken);
    }

    /// <summary>Sends a scroll gesture in pixels the page should move.</summary>
    public Task SendScrollAsync(
        int x,
        int y,
        int deltaX,
        int deltaY,
        long navigationId,
        long frameId,
        CancellationToken cancellationToken)
    {
        // A zero-delta scroll is a gesture that has not moved yet, not an event worth a round trip.
        if (!host.CanSendTouch || (deltaX == 0 && deltaY == 0))
        {
            return Task.CompletedTask;
        }

        var (nav, frame) = ResolveRefs(navigationId, frameId);
        if (nav <= 0 || frame <= 0)
        {
            return Task.CompletedTask;
        }

        return SendAsync(new BrowserScrollInput(nav, frame, x, y, deltaX, deltaY), cancellationToken);
    }

    /// <summary>Sends one semantic key — up, select, page down — never a platform key code.</summary>
    public Task SendKeyAsync(BrowserSemanticKey key, CancellationToken cancellationToken) =>
        SendAsync(new BrowserSemanticKeyInput(key), cancellationToken);

    /// <summary>Sends text whole. A phrase sent letter by letter arrives behind the typing.</summary>
    public Task SendTextAsync(string? text, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(text)
            ? Task.CompletedTask
            : SendAsync(new BrowserTextInput(text), cancellationToken);

    private (long NavigationId, long FrameId) ResolveRefs(long navigationId, long frameId)
    {
        if (navigationId > 0 && frameId > 0)
        {
            return (navigationId, frameId);
        }

        return BrowserPreviewInteraction.ResolveRefs(
            host.Preview.IsLive && host.Preview.HasFrame,
            host.Preview.NavigationId,
            host.Preview.FrameId,
            host.ActiveNavigationId);
    }

    /// <summary>
    /// Sends one input, numbering it so the receiver will accept it.
    /// </summary>
    /// <remarks>
    /// Silent when there is no live session. Keyboard focus can sit on the page before anything is
    /// connected and after a television has gone to sleep, and neither is worth an error: the user
    /// pressed a key on a page that is plainly not connected, and the page already says so.
    /// </remarks>
    private async Task SendAsync(BrowserInputEvent input, CancellationToken cancellationToken)
    {
        if (host.Session is not { IsConnected: true } session)
        {
            return;
        }

        try
        {
            var sequence = nextSequence++;
            await session.SendInputAsync(
                    new BrowserInputMessage(host.Epoch, sequence, input),
                    cancellationToken)
                .ConfigureAwait(true);
            host.ReportInputResult(null);
            Log(sequence, input);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            host.ReportInputResult(BrowserVerdictCopy.SafeMessage(exception));
            Flint.Core.FlintDiag.Warn("FlintBrowser", $"input send failed: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// High-level input breadcrumbs. Pointer MOVE is omitted so aim-pad spam does not drown the log;
    /// DOWN/UP, scroll, keys and text length still reconstruct what the user did.
    /// </summary>
    private void Log(long sequence, BrowserInputEvent input)
    {
        switch (input)
        {
            case BrowserPointerInput pointer:
                if (pointer.Action == BrowserPointerAction.Move
                    && lastLoggedPointerAction == BrowserPointerAction.Move)
                {
                    return;
                }

                lastLoggedPointerAction = pointer.Action;
                Flint.Core.FlintDiag.Info(
                    "FlintBrowser",
                    $"input pointer seq={sequence} action={pointer.Action} navId={pointer.NavigationId} x={pointer.X} y={pointer.Y} buttons={pointer.Buttons}");
                break;
            case BrowserScrollInput scroll:
                Flint.Core.FlintDiag.Info(
                    "FlintBrowser",
                    $"input scroll seq={sequence} navId={scroll.NavigationId} dx={scroll.DeltaX} dy={scroll.DeltaY}");
                break;
            case BrowserSemanticKeyInput key:
                Flint.Core.FlintDiag.Info("FlintBrowser", $"input key seq={sequence} key={key.Key}");
                break;
            case BrowserTextInput text:
                Flint.Core.FlintDiag.Info(
                    "FlintBrowser",
                    $"input text seq={sequence} len={text.Text.Length}");
                break;
            default:
                if (sequence != lastLoggedSequence)
                {
                    lastLoggedSequence = sequence;
                    Flint.Core.FlintDiag.Info(
                        "FlintBrowser",
                        $"input other seq={sequence} type={input.GetType().Name}");
                }

                break;
        }
    }
}
