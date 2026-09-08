using Flint.Core.Tests.TestData;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>
/// The browser eligibility decision is deliberately independent from the existing cast-mode
/// report. These examples cover the precedence rules that stop an optimistic UI from appearing
/// before the receiver has proved every browser-specific prerequisite.
/// </summary>
public sealed class BrowserCapabilityAssessorTests
{
    [Fact]
    public void Assess_NoSelectedReceiver_IsUnsupportedWithoutAnOptimisticRemedy()
    {
        var capability = BrowserCapabilityAssessor.Assess(null);

        capability.Availability.ShouldBe(BrowserAvailability.UnsupportedPlatform);
        capability.IsEligible.ShouldBeFalse();
        capability.Reason.ShouldContain("not verified");
        capability.Remedy.ShouldNotBeNull();
    }

    [Fact]
    public void Assess_UnknownPlatformWithoutBrowserEvidence_StaysUnsupported()
    {
        var capability = BrowserCapabilityAssessor.Assess(
            Build.Device(platform: FireTvPlatform.Unknown));

        capability.Availability.ShouldBe(BrowserAvailability.UnsupportedPlatform);
        capability.IsEligible.ShouldBeFalse();
    }

    [Fact]
    public void Assess_UnknownPlatformWithSecureBrowserAdvertisement_IsEligible()
    {
        // ADB may be refused while the receiver still advertises browser_port. That advertisement
        // is enough to offer Verify; TLS pin comparison remains a separate step.
        var capability = BrowserCapabilityAssessor.Assess(
            Build.Device(platform: FireTvPlatform.Unknown) with
            {
                BrowserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Passed)
                {
                    SecureEndpointPort = 33164,
                },
            });

        capability.Availability.ShouldBe(BrowserAvailability.Available);
        capability.IsEligible.ShouldBeTrue();
    }

    [Fact]
    public void Assess_VegaReceiver_IsPermanentlyUnsupported()
    {
        var capability = BrowserCapabilityAssessor.Assess(
            Build.Device(platform: FireTvPlatform.Vega));

        capability.Availability.ShouldBe(BrowserAvailability.UnsupportedPlatform);
        capability.IsEligible.ShouldBeFalse();
        capability.Remedy.ShouldBeNull();
    }

    [Fact]
    public void Assess_V1Receiver_IsProtocolTooOldEvenWhenItsWebViewProbePassed()
    {
        var receiver = Build.Device() with
        {
            BrowserEvidence = new BrowserReceiverEvidence(
                MaximumProtocolVersion: 1,
                SecureEndpointAvailable: true,
                WebViewProbe: BrowserWebViewProbe.Passed),
        };

        var capability = BrowserCapabilityAssessor.Assess(receiver);

        capability.Availability.ShouldBe(BrowserAvailability.ProtocolTooOld);
        capability.IsEligible.ShouldBeFalse();
        capability.Remedy.ShouldNotBeNull();
        capability.Remedy!.ShouldContain("receiver");
    }

    [Fact]
    public void Assess_MissingSecureEndpoint_BlocksAProtocolV2Receiver()
    {
        var receiver = Build.Device() with
        {
            BrowserEvidence = new BrowserReceiverEvidence(
                MaximumProtocolVersion: 2,
                SecureEndpointAvailable: false,
                WebViewProbe: BrowserWebViewProbe.Passed),
        };

        var capability = BrowserCapabilityAssessor.Assess(receiver);

        capability.Availability.ShouldBe(BrowserAvailability.SecureEndpointUnavailable);
        capability.IsEligible.ShouldBeFalse();
        capability.Reason.ShouldNotContain("pairing code");
    }

    [Theory]
    [InlineData(BrowserWebViewProbe.NotRun)]
    [InlineData(BrowserWebViewProbe.Running)]
    [InlineData(BrowserWebViewProbe.Inconclusive)]
    public void Assess_UnknownWebViewEvidence_RemainsPending(BrowserWebViewProbe probe)
    {
        var receiver = Build.Device() with
        {
            BrowserEvidence = new BrowserReceiverEvidence(2, true, probe),
        };

        var capability = BrowserCapabilityAssessor.Assess(receiver);

        capability.Availability.ShouldBe(BrowserAvailability.WebViewSmokeTestPending);
        capability.IsEligible.ShouldBeFalse();
        capability.Remedy.ShouldNotBeNull();
    }

    [Fact]
    public void Assess_FailedWebViewProbe_IsNotOfferable()
    {
        var receiver = Build.Device() with
        {
            BrowserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Unsupported),
        };

        var capability = BrowserCapabilityAssessor.Assess(receiver);

        capability.Availability.ShouldBe(BrowserAvailability.WebViewUnsupported);
        capability.IsEligible.ShouldBeFalse();
        capability.Remedy.ShouldBeNull();
    }

    [Fact]
    public void Assess_AllEvidencePassed_IsEligibleButDoesNotClaimAWebsiteLoaded()
    {
        var receiver = Build.Device() with
        {
            BrowserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Passed),
        };

        var capability = BrowserCapabilityAssessor.Assess(receiver);

        capability.Availability.ShouldBe(BrowserAvailability.Available);
        capability.IsEligible.ShouldBeTrue();
        capability.Reason.ShouldContain("eligible");
        capability.Reason.ShouldNotContain("loaded");
    }

    [Fact]
    public void CapabilityReport_CarriesASeparateBrowserVerdictWithoutChangingCastModeCount()
    {
        var receiver = Build.Device() with
        {
            BrowserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Passed),
        };

        var report = CapabilityAssessor.Assess(receiver, Build.Host(), Build.Path());

        report.Verdicts.Count.ShouldBe(Enum.GetValues<CastMode>().Length);
        report.Browser.Availability.ShouldBe(BrowserAvailability.Available);
    }

    [Fact]
    public void EveryAvailabilityHasBoundedUserFacingCopy()
    {
        foreach (var availability in Enum.GetValues<BrowserAvailability>())
        {
            var copy = BrowserCapabilityCopy.For(availability);

            copy.Reason.ShouldNotBeNullOrWhiteSpace();
            copy.Reason.Length.ShouldBeLessThanOrEqualTo(BrowserCapabilityCopy.MaximumCopyLength);
            if (availability == BrowserAvailability.WebViewUnsupported)
            {
                copy.Remedy.ShouldBeNull();
            }
            else
            {
                copy.Remedy.ShouldNotBeNullOrWhiteSpace();
                copy.Remedy!.Length.ShouldBeLessThanOrEqualTo(BrowserCapabilityCopy.MaximumCopyLength);
            }
        }
    }
}
