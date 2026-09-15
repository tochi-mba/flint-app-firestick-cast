using Flint.App.ViewModels;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The Web page must not put a form in front of someone who has nothing to fill in.
/// </summary>
/// <remarks>
/// Reported from a real screen: a returning user saw a SECURE RECEIVER card with an empty port box,
/// a greyed-out CONNECT button, and a next step reading "enter the Cast pairing code" — on a page
/// with no pairing-code field. Every input that mattered was either already known or lived on
/// another page, and nothing said which.
/// </remarks>
public sealed class BrowserSecureReceiverPromptTests
{
    [Fact]
    public void Step_WithEverythingPresentAndTrustRemembered_AsksForNothing()
    {
        // The reconnect path handles this case unattended. Anything shown here is a step the person
        // would reasonably believe they have to complete.
        var step = BrowserSecureReceiverPrompt.Step(
            BrowserUiPhase.Idle,
            isEligible: true,
            hasRememberedTrust: true,
            hasPairingCode: true,
            hasRoutableEndpoint: true);

        step.ShouldBe(BrowserSecureReceiverStep.None);
        BrowserSecureReceiverPrompt.ShowsCard(BrowserUiPhase.Idle, step).ShouldBeFalse();
        BrowserSecureReceiverPrompt.Remedy(step, capabilityRemedy: "unused").ShouldBeNull();
    }

    [Fact]
    public void Step_WithoutARememberedPin_StillRequiresAPersonToCompareCodes()
    {
        // First use is the one step that cannot be automated: a human decides the two codes match.
        var step = BrowserSecureReceiverPrompt.Step(
            BrowserUiPhase.Idle,
            isEligible: true,
            hasRememberedTrust: false,
            hasPairingCode: true,
            hasRoutableEndpoint: true);

        step.ShouldBe(BrowserSecureReceiverStep.CompareSecurityCodes);
        BrowserSecureReceiverPrompt.ShowsCard(BrowserUiPhase.Idle, step).ShouldBeTrue();
    }

    [Fact]
    public void Remedy_WhenThePairingCodeIsMissing_SaysWhereTheCodeIsEntered()
    {
        // The defect this test exists for: the old copy asked for a code next to no field for one.
        var step = BrowserSecureReceiverPrompt.Step(
            BrowserUiPhase.Idle,
            isEligible: true,
            hasRememberedTrust: true,
            hasPairingCode: false,
            hasRoutableEndpoint: true);

        step.ShouldBe(BrowserSecureReceiverStep.EnterPairingCode);

        var remedy = BrowserSecureReceiverPrompt.Remedy(step, capabilityRemedy: null);
        remedy.ShouldNotBeNull();
        remedy.ShouldContain("Cast page");
    }

    [Fact]
    public void Step_WithNoRoutableEndpoint_AsksForThePortBeforeTheCode()
    {
        // Ordered the way a person supplies them: without routing there is nothing for a code to
        // authenticate against, so asking for the code first would be asking twice.
        var step = BrowserSecureReceiverPrompt.Step(
            BrowserUiPhase.Idle,
            isEligible: true,
            hasRememberedTrust: true,
            hasPairingCode: false,
            hasRoutableEndpoint: false);

        step.ShouldBe(BrowserSecureReceiverStep.EnterBrowserPort);
    }

    [Fact]
    public void Step_WithAnIneligibleReceiver_DefersToTheCapabilityVerdictsOwnWords()
    {
        // One account of a device, not two: the page repeats the verdict rather than paraphrasing.
        var step = BrowserSecureReceiverPrompt.Step(
            BrowserUiPhase.Idle,
            isEligible: false,
            hasRememberedTrust: true,
            hasPairingCode: true,
            hasRoutableEndpoint: true);

        step.ShouldBe(BrowserSecureReceiverStep.ChooseReceiver);
        BrowserSecureReceiverPrompt.Remedy(step, capabilityRemedy: "verdict copy").ShouldBe("verdict copy");
    }

    [Theory]
    [InlineData(BrowserUiPhase.SecureReady)]
    [InlineData(BrowserUiPhase.Verifying)]
    public void Step_OnceAHandshakeIsUnderwayOrDone_AsksForNothing(BrowserUiPhase phase)
    {
        BrowserSecureReceiverPrompt
            .Step(phase, isEligible: false, hasRememberedTrust: false, hasPairingCode: false, hasRoutableEndpoint: false)
            .ShouldBe(BrowserSecureReceiverStep.None);
    }

    [Fact]
    public void ShowsCard_AfterAMismatch_KeepsTheRetryReachable()
    {
        // Automatic reconnect deliberately refuses to retry a failed verification, so if the card
        // vanished on "nothing missing" there would be no way back.
        BrowserSecureReceiverPrompt
            .ShowsCard(BrowserUiPhase.Mismatch, BrowserSecureReceiverStep.None)
            .ShouldBeTrue();
    }

    [Fact]
    public void ShowsPortEntry_OnceTheTelevisionHasAdvertisedItsPort_IsHidden()
    {
        // The box stands in for discovery. When discovery worked it is a filled field nobody should
        // touch, sitting between the person and a session that is already reachable.
        BrowserSecureReceiverPrompt.ShowsPortEntry(portWasAdvertised: true).ShouldBeFalse();
        BrowserSecureReceiverPrompt.ShowsPortEntry(portWasAdvertised: false).ShouldBeTrue();
    }

    [Fact]
    public void Instructions_SeparateStandingTrustFromTheNextStep()
    {
        var returning = BrowserSecureReceiverPrompt.Instructions(
            BrowserSecureReceiverStep.EnterPairingCode, hasRememberedTrust: true);

        returning.ShouldContain("verified before");
        returning.ShouldContain("Cast page");

        var settled = BrowserSecureReceiverPrompt.Instructions(
            BrowserSecureReceiverStep.None, hasRememberedTrust: true);

        settled.ShouldContain("verified before");
        settled.ShouldNotContain("Cast page");
    }
}
