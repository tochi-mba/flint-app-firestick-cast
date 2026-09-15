namespace Flint.App.ViewModels;

/// <summary>What, if anything, a person still has to do before a secure browser session exists.</summary>
internal enum BrowserSecureReceiverStep
{
    /// <summary>Nothing is missing. Reconnect happens on its own; asking for a click would be theatre.</summary>
    None = 0,

    /// <summary>No eligible receiver has been found, so there is nothing to connect to yet.</summary>
    ChooseReceiver = 1,

    /// <summary>Eligible, but discovery never advertised the browser port, so routing is unknown.</summary>
    EnterBrowserPort = 2,

    /// <summary>Routing is known, but the Cast pairing code that proves the TV's identity is absent.</summary>
    EnterPairingCode = 3,

    /// <summary>First use: two security codes have to be compared by a person, and only a person can.</summary>
    CompareSecurityCodes = 4,
}

/// <summary>
/// The one decision the Web page's secure-receiver card exists to serve.
/// </summary>
/// <remarks>
/// The card used to be unconditional: a heading, an instruction, a port box and a button, shown
/// whenever a device was selected. That put a form in front of people at moments when every input
/// was already present and Windows was about to reconnect by itself, and — worse — it printed a next
/// step ("enter the Cast pairing code") next to no field that could accept one, because the pairing
/// code lives on the Cast page.
///
/// So the card is now driven by what is actually missing. When nothing is, it does not appear at
/// all. When something is, the copy names it and says where it is entered.
///
/// Pure, so every branch is testable without a session, a television, or a window.
/// </remarks>
internal static class BrowserSecureReceiverPrompt
{
    /// <summary>The first missing input, in the order a person would supply them.</summary>
    internal static BrowserSecureReceiverStep Step(
        BrowserUiPhase phase,
        bool isEligible,
        bool hasRememberedTrust,
        bool hasPairingCode,
        bool hasRoutableEndpoint)
    {
        // Mid-handshake and after it, there is nothing to ask for.
        if (phase is BrowserUiPhase.SecureReady or BrowserUiPhase.Verifying)
        {
            return BrowserSecureReceiverStep.None;
        }

        if (!isEligible)
        {
            return BrowserSecureReceiverStep.ChooseReceiver;
        }

        if (!hasRoutableEndpoint)
        {
            return BrowserSecureReceiverStep.EnterBrowserPort;
        }

        if (!hasPairingCode)
        {
            return BrowserSecureReceiverStep.EnterPairingCode;
        }

        // Everything is present. A remembered pin means Windows reconnects unattended; without one,
        // the fingerprint comparison is a human judgement that cannot be automated away.
        return hasRememberedTrust
            ? BrowserSecureReceiverStep.None
            : BrowserSecureReceiverStep.CompareSecurityCodes;
    }

    /// <summary>
    /// Whether the card should occupy space.
    /// </summary>
    /// <remarks>
    /// A mismatch keeps the card even with nothing missing: automatic reconnect deliberately does
    /// not retry a failed verification, so the retry has to be reachable.
    /// </remarks>
    internal static bool ShowsCard(BrowserUiPhase phase, BrowserSecureReceiverStep step) =>
        phase is BrowserUiPhase.Mismatch || step is not BrowserSecureReceiverStep.None;

    /// <summary>
    /// Whether the browser-port box earns its place.
    /// </summary>
    /// <remarks>
    /// The field is a stand-in for discovery, not a routine step. Once the TV has advertised its
    /// port there is nothing for a person to decide, and a filled-in box they must not touch reads
    /// as another thing standing between them and a working session.
    /// </remarks>
    internal static bool ShowsPortEntry(bool portWasAdvertised) => !portWasAdvertised;

    /// <summary>The next step, naming where it is done — or null when there is nothing to do.</summary>
    /// <param name="capabilityRemedy">
    /// The capability verdict's own words, used verbatim for a receiver Flint has not accepted, so
    /// the page and diagnostics never give two accounts of the same device.
    /// </param>
    internal static string? Remedy(BrowserSecureReceiverStep step, string? capabilityRemedy) => step switch
    {
        BrowserSecureReceiverStep.ChooseReceiver => capabilityRemedy,
        BrowserSecureReceiverStep.EnterBrowserPort =>
            "Discovery did not advertise a browser port. Type the BROWSER PORT shown on the TV's Flint Receiver screen into the box below.",
        BrowserSecureReceiverStep.EnterPairingCode =>
            "Enter the six-digit Cast pairing code on the Cast page. Windows reconnects this receiver on its own once the code is there.",
        BrowserSecureReceiverStep.CompareSecurityCodes =>
            "Compare the security code on the TV with the one shown here, then confirm to pin this receiver. Flint asks once per receiver.",
        _ => null,
    };

    /// <summary>
    /// The card's own instruction line: what this receiver is, then what to do about it.
    /// </summary>
    /// <remarks>
    /// The trust half and the step half are separate facts and were previously fused into one
    /// sentence per trust state, which is how "enter the Cast pairing code" ended up printed on a
    /// card that has no pairing-code field regardless of whether a code was already present.
    /// </remarks>
    internal static string Instructions(BrowserSecureReceiverStep step, bool hasRememberedTrust)
    {
        var standing = hasRememberedTrust
            ? "This TV was verified before, so Flint reconnects its pinned TLS session without comparing security codes again."
            : "This TV has not been verified on this PC yet.";

        return Remedy(step, capabilityRemedy: null) is { } next
            ? $"{standing} {next}"
            : standing;
    }

    /// <summary>What the page says about itself while an unattended reconnect is in flight.</summary>
    internal const string ReconnectingReason =
        "Reconnecting the pinned TLS browser session — this receiver was verified before, so no codes are compared again.";
}
