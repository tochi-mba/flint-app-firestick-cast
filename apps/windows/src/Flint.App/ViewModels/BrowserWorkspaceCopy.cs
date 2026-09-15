namespace Flint.App.ViewModels;

/// <summary>
/// What the mosaic panel says about itself, in one place.
/// </summary>
/// <remarks>
/// The workspace has more waiting states than anything else on this page — a focus request the
/// television has not confirmed, a layout change in flight, panes being opened before a split can
/// be applied — and each one has to say which of them it is. Held together so the set can be read
/// as a set: the failure this guards against is two of these states rendering the same words, and
/// a viewer being unable to tell a request that is progressing from one that is stuck.
///
/// Pure functions of the state passed in, so every line is testable without a television.
/// </remarks>
internal static class BrowserWorkspaceCopy
{
    /// <summary>The status strip above the mosaic.</summary>
    internal static string StatusLabel(
        bool isAvailable,
        bool focusRequestPending,
        bool arrangementPending,
        bool layoutRequestPending,
        int paneCount,
        BrowserWorkspaceLayout layout) => (isAvailable, focusRequestPending, arrangementPending) switch
        {
            (false, _, _) => "WORKSPACE UNAVAILABLE",
            (_, true, _) => "FOCUS REQUEST SENT · INPUT BLOCKED",
            (_, _, true) => paneCount == 0
                ? "OPENING MOSAIC · LEAVING TABS"
                : "OPENING PAGES FOR SPLIT · WAITING FOR TV",
            _ when layoutRequestPending => "LAYOUT REQUEST SENT · WAITING FOR TV",
            _ => paneCount switch
            {
                0 => "NO PAGES YET · PRESS SPLIT VIEW",
                1 => "1 PAGE · SPLIT VIEW ADDS A SECOND BESIDE IT",
                2 when layout == BrowserWorkspaceLayout.Single => "2 PAGES · PRESS SPLIT VIEW OR STACK",
                _ => $"{paneCount} PAGES · {layout.DisplayName().ToUpperInvariant()}",
            },
        };

    /// <summary>
    /// Longer coach copy — the difference between tabs and the mosaic.
    /// </summary>
    /// <remarks>
    /// Spelled out because the two look similar and are not: a tab replaces what is on the glass,
    /// a pane sits beside it. People who assume the first will read a mosaic as a bug.
    /// </remarks>
    internal const string CoachHint =
        "Tabs switch one page at a time. Split View / Stack open a second independent page beside or above the first (the mosaic). That is separate from the tab strip.";

    /// <summary>Why the panel is showing nothing, when the receiver has not advertised panes.</summary>
    internal const string Unavailable =
        "This TV has not advertised a multi-page workspace yet.";

    /// <summary>The same, with the reason Flint will not improvise something in its place.</summary>
    internal const string UnavailableDetail =
        "This receiver has not advertised independent browser workspace panes. Flint will not present video tiles as pages.";

    /// <summary>
    /// Why desktop text entry is open, or exactly what it is waiting for.
    /// </summary>
    /// <remarks>
    /// Input is held behind a focus the television has confirmed, so this has to distinguish "not
    /// supported", "nothing selected", "waiting for confirmation" and "not connected". Collapsing
    /// any two of them leaves a viewer with a dead text box and no idea which.
    /// </remarks>
    internal static string InputStatus(
        bool isAvailable,
        string? pendingFocusPaneName,
        bool canSendInput,
        string? confirmedFocusPaneName,
        bool channelConnected) => (isAvailable, pendingFocusPaneName, canSendInput) switch
        {
            (false, _, _) => "INPUT UNAVAILABLE UNTIL THE TV ADVERTISES A WORKSPACE",
            (_, { } pending, _) => $"WAITING FOR TV TO CONFIRM FOCUS ON {pending} · INPUT BLOCKED",
            (_, _, false) => "THIS TV DID NOT ADVERTISE WORKSPACE TEXT INPUT",
            _ when confirmedFocusPaneName is not { } focused => "SELECT A PANE BEFORE SENDING INPUT",
            _ => channelConnected
                ? $"INPUT TARGET: {confirmedFocusPaneName} · CONFIRMED BY TV"
                : "TV WORKSPACE CHANNEL IS NOT CONNECTED",
        };

    /// <summary>Why one layout button is offered, current, or refused.</summary>
    internal static string LayoutAvailability(
        BrowserWorkspaceLayout layout,
        bool advertisedByTv,
        bool fitsPaneCount,
        BrowserWorkspaceLayout currentLayout)
    {
        if (!advertisedByTv)
        {
            return "NOT SUPPORTED BY THIS TV";
        }

        if (!fitsPaneCount)
        {
            return layout switch
            {
                BrowserWorkspaceLayout.Single => "ONLY WHEN ONE PANE IS OPEN",
                BrowserWorkspaceLayout.TwoColumns or BrowserWorkspaceLayout.TwoRows =>
                    "OPEN A SECOND PANE FIRST",
                BrowserWorkspaceLayout.FourGrid => "NEED 3 OR 4 OPEN PANES",
                _ => "DOES NOT FIT OPEN PANES",
            };
        }

        return currentLayout == layout ? "CURRENT TV LAYOUT" : "AVAILABLE ON THIS TV";
    }
}
