using System.IO;
using Flint.Protocol;

namespace Flint.App.ViewModels;

/// <summary>
/// The commands that ride the session but are not navigation and not input.
/// </summary>
/// <remarks>
/// Preview and dialogs end up together because they share the same failure discipline: both must
/// leave the page telling the truth when the television never heard them. A preview toggle that
/// stays "on" over a dead channel, or a dialog cleared locally while the television still blocks on
/// it, are the same bug wearing different clothes.
/// </remarks>
internal sealed class BrowserSessionCommands(BrowserPageViewModel page)
{
    /// <summary>
    /// Turns the television preview on or off for the rest of this session.
    /// </summary>
    /// <remarks>
    /// The requested flag moves first so the control responds immediately, and is put back if the
    /// send fails. Optimism is fine; keeping it after a refusal is not.
    /// </remarks>
    public async Task TogglePreviewAsync(CancellationToken cancellationToken)
    {
        if (page.Session is null)
        {
            return;
        }

        var requested = !page.Preview.IsRequested;
        page.Preview.SetRequested(requested);
        try
        {
            await SendPreviewEnabledAsync(requested, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            page.Preview.SetRequested(!requested);
            page.ReportError(BrowserUiError.FromException(exception, BrowserUiErrorKind.Preview));
        }
    }

    /// <summary>
    /// Asks the television to match the host's preview preference once an epoch exists.
    /// </summary>
    /// <remarks>
    /// Preview is on by default where the receiver can capture, and a reconnect can adopt a page
    /// that is already open — so the request has to be repeated at that point rather than assumed
    /// from the earlier session. Failure is logged, not surfaced: nobody asked for this one.
    /// </remarks>
    public async Task SyncPreferenceAsync(CancellationToken cancellationToken)
    {
        if (page.Session is null || !page.Preview.IsSupported || !page.Preview.IsRequested
            || page.Epoch <= 0)
        {
            return;
        }

        try
        {
            await SendPreviewEnabledAsync(true, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            Flint.Core.FlintDiag.Warn(
                "FlintBrowser",
                $"default SetPreviewEnabled failed: {exception.GetType().Name}");
        }
    }

    private async Task SendPreviewEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (page.Session is not { } session)
        {
            return;
        }

        var commandId = page.NextCommandId++;
        await session.SendCommandAsync(
            new BrowserCommandMessage(
                page.Epoch,
                commandId,
                BrowserCommandAction.SetPreviewEnabled,
                PreviewEnabled: enabled),
            cancellationToken).ConfigureAwait(true);
        Flint.Core.FlintDiag.Info(
            "FlintBrowser",
            $"command SetPreviewEnabled enabled={enabled} cmdId={commandId}");
    }

    /// <summary>
    /// Answers the one native dialog the page is holding open.
    /// </summary>
    /// <remarks>
    /// The television shows the same dialog and can answer it alone; this is the second way in, not
    /// the only one. A dialog nobody can answer blocks the page, which is why an unanswerable one
    /// is worse than an ugly one.
    /// </remarks>
    public async Task AnswerDialogAsync(bool accepted, CancellationToken cancellationToken)
    {
        if (page.Session is not { } session || page.Dialog.Active is not { } active)
        {
            return;
        }

        // Prompt text only travels with an acceptance. A cancelled prompt carries nothing, which is
        // what the receiver's reducer requires and what keeps a refusal from leaking a draft answer.
        var promptText = accepted && page.Dialog.IsPrompt ? page.Dialog.PromptText : null;
        page.Dialog.Clear();
        try
        {
            await session.SendDialogReplyAsync(
                new BrowserDialogReplyMessage(active.Epoch, active.DialogId, accepted, promptText),
                cancellationToken).ConfigureAwait(true);
            Flint.Core.FlintDiag.Info(
                "FlintBrowser",
                $"dialog reply id={active.DialogId} accepted={accepted} promptLen={promptText?.Length ?? 0}");
        }
        catch (Exception exception) when (exception is IOException or ArgumentException)
        {
            page.ReportError(BrowserUiError.FromException(exception, BrowserUiErrorKind.Connection));
        }
    }
}
