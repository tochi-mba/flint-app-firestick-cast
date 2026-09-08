using Flint.Core.Tests.TestData;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>
/// The assessor is what stops Flint lying to the user, so these tests assert on the wording of the
/// verdicts as well as their status. A correct status with a misleading sentence is still a defect.
/// </summary>
public sealed class CapabilityAssessorTests
{
    [Fact]
    public void Assess_VegaDevice_ReportsMirroringImpossibleRatherThanBlocked()
    {
        // Arrange
        var device = Build.Device(FireTvPlatform.Vega, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Impossible);
        report[CastMode.Mirror].Reason.ShouldContain("Vega OS");
    }

    [Fact]
    public void Assess_VegaDevice_OffersNoRemedyBecauseThereIsNone()
    {
        // Arrange
        var device = Build.Device(FireTvPlatform.Vega, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report[CastMode.Mirror].Remedy.ShouldBeNull();
    }

    [Fact]
    public void Assess_VegaDevice_BlocksEveryMode()
    {
        // Arrange
        var device = Build.Device(FireTvPlatform.Vega, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report.Verdicts.ShouldAllBe(verdict => verdict.Status == ModeStatus.Impossible);
        report.HasAnyAvailableMode.ShouldBeFalse();
    }

    [Fact]
    public void Assess_VegaDevice_PreservesTheModeOnEachVerdict()
    {
        // A placeholder mode leaking out of the shared receiver check would mis-label the UI.

        // Arrange
        var device = Build.Device(FireTvPlatform.Vega, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report.Verdicts.Select(verdict => verdict.Mode)
            .ShouldBe([CastMode.Mirror, CastMode.SecondScreen, CastMode.MediaHandoff]);
    }

    [Fact]
    public void Assess_NothingAnsweredAtTheAddress_DoesNotClaimFlintReachedIt()
    {
        // The failure a person actually hits: a mistyped or stale address with no device behind it.
        // Saying "Flint reached 10.230.19.172" there is simply false, and the ADB-debugging remedy
        // sends them into Developer Options on a television that was never the problem.

        // Arrange
        var device = Build.Device(FireTvPlatform.Unknown, AdbConnectionState.TimedOut);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Reason.ShouldNotContain("Flint reached");
        mirror.Reason.ShouldContain("Nothing answered");
        // The first thing to check is the address, not the TV's developer settings.
        mirror.Remedy!.ShouldContain("About");
        mirror.Remedy!.ShouldNotContain("seven times");
    }

    [Fact]
    public void Assess_PortRefusedTheConnection_StillOffersTheAdbRemedy()
    {
        // Something is there and actively refused the port. That genuinely is the ambiguous
        // Vega-or-debugging-off case, and the developer-options remedy belongs here.

        // Arrange
        var device = Build.Device(FireTvPlatform.Unknown, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report[CastMode.Mirror].Remedy!.ShouldContain("seven times");
    }

    [Fact]
    public void Assess_SilentAdbPort_RefusesToGuessBetweenVegaAndDisabledDebugging()
    {
        // The whole point of the prober: these two cases are indistinguishable from the network.

        // Arrange
        var device = Build.Device(FireTvPlatform.Unknown, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Reason.ShouldContain("refused the ADB port");
        // A refusal proves the address is right, so it must not blame the address.
        mirror.Reason.ShouldNotContain("address may be wrong");
        mirror.Remedy!.ShouldContain("seven times");
        mirror.Remedy.ShouldNotBeNull();
    }

    [Fact]
    public void Assess_PairedSessionWithUnknownPlatform_NoLongerBlocksMediaOnAdbInstallStory()
    {
        // Pair proved the Flint receiver is live. Repeating the ADB-install BLOCKED blurb on every
        // mode card after that is a lie — Media at least must follow the live session.
        var device = Build.Device(FireTvPlatform.Unknown, AdbConnectionState.Refused);

        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path(), pairedSessionActive: true);

        report[CastMode.MediaHandoff].Status.ShouldBe(ModeStatus.Available);
        report[CastMode.MediaHandoff].Reason.ShouldNotContain("ADB did not answer");
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Available);
    }

    [Fact]
    public void Assess_SilentAdbPort_IsBlockedNotImpossibleBecauseTheUserMayBeAbleToFixIt()
    {
        // Arrange
        var device = Build.Device(FireTvPlatform.Unknown, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report[CastMode.Mirror].Status.ShouldNotBe(ModeStatus.Impossible);
    }

    [Fact]
    public void Assess_UnauthorizedDevice_PointsAtTheTelevisionPrompt()
    {
        // Arrange
        var device = Build.Device(FireTvPlatform.FireOs8, AdbConnectionState.Unauthorized);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Remedy.ShouldNotBeNull();
        mirror.Remedy.ShouldContain("authorisation prompt");
    }

    [Fact]
    public void Assess_NoDeviceFound_BlocksWithoutClaimingAnythingAboutHardware()
    {
        // Act
        var report = CapabilityAssessor.Assess(device: null, Build.Host(), path: null);

        // Assert
        report.Device.ShouldBeNull();
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Blocked);
        report[CastMode.Mirror].Reason.ShouldNotContain("Vega");
    }

    [Fact]
    public void Assess_UnknownDirectAddress_ExplainsThatAdbIsTheBlockingDependency()
    {
        var device = Build.Device(FireTvPlatform.Unknown, AdbConnectionState.Refused);

        var report = CapabilityAssessor.Assess(device, Build.Host(), path: null);

        report[CastMode.Mirror].Reason.ShouldContain(device.Address.ToString());
        report[CastMode.Mirror].Reason.ShouldContain("refused the ADB port");
        report[CastMode.Mirror].Remedy.ShouldNotBeNull();
        report[CastMode.Mirror].Remedy!.ShouldContain("seven times");
    }

    [Fact]
    public void Assess_HealthyFireOsPair_OffersMirroring()
    {
        // The capture to encode to transport path now exists and has been proven against a Fire TV
        // with the desktop visibly on screen, so a pair with good hardware and a good network is
        // offered the mode rather than told to wait for a Flint update.

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Available);
        report[CastMode.Mirror].IsOfferable.ShouldBeTrue();
    }

    [Fact]
    public void Assess_MirrorAvailable_OffersNoRemedyBecauseNothingNeedsFixing()
    {
        // A remedy alongside a working mode would send someone hunting through driver updates for
        // a problem they do not have.

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        report[CastMode.Mirror].Remedy.ShouldBeNull();
    }

    [Fact]
    public void Assess_Mirror_StillSurfacesARealHardwareProblemFirst()
    {
        // "Not built yet" is the terminal verdict only once nothing else is wrong. A PC with no
        // hardware encoder has a genuine, actionable problem of its own, and telling that person
        // to wait for a Flint update would hide something they could fix today.

        // Arrange: no session-capable encoder, which is independently blocking regardless of
        // whether streaming has been written yet.
        var host = Build.Host([Build.SoftwareOnly()]);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Blocked);
        report[CastMode.Mirror].Reason.ShouldContain("encoder");
    }

    [Fact]
    public void Assess_MirrorAvailable_OnlyAppearsOnceHardwareAndNetworkBothCheckOut()
    {
        // The flip side of the test above: a working code path does not make a mode offerable on
        // its own. Every hardware and network check still has to pass first.

        // Arrange: a path too slow to mirror over, on an otherwise perfect pair.
        var path = Build.Path(throughputMbps: NetworkPath.MinimumMirrorThroughputMbps - 1.0);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Blocked);
        report[CastMode.Mirror].IsOfferable.ShouldBeFalse();
    }

    [Fact]
    public void Assess_SecondScreen_IsStillUnbuiltEvenThoughMirroringWorks()
    {
        // A second screen is a mirror plus an indirect display driver. Proving the mirror works
        // says nothing about that driver, which does not exist in this build — so the two are
        // judged separately rather than the second screen inheriting the mirror's verdict.

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Available);
        report[CastMode.SecondScreen].Status.ShouldBe(ModeStatus.NotImplemented);
        report[CastMode.SecondScreen].Reason.ShouldContain("display driver");
    }

    [Fact]
    public void Assess_SecondScreen_SaysWhatTheModeDoesBeforeWhatItLacks()
    {
        // A card that opens with a missing driver reads as a PC that can never run the mode. It
        // should say what the feature is first, then that it is simply not here yet.

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        var reason = report[CastMode.SecondScreen].Reason;
        reason.ShouldContain("extra desktop");
        reason.IndexOf("extra desktop", StringComparison.Ordinal)
            .ShouldBeLessThan(reason.IndexOf("display driver", StringComparison.Ordinal));
    }

    [Fact]
    public void Assess_SecondScreen_CreditsThePairForTheMirroringItCanAlreadyDo()
    {
        // Half of a second screen is a mirror, and this pair has that half working. Saying so
        // stops the mode reading as entirely out of reach.

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        report[CastMode.SecondScreen].Reason.ShouldContain("already mirror");
    }

    [Fact]
    public void Assess_SecondScreenOnAPcThatCannotMirror_DoesNotClaimItAlreadyMirrors()
    {
        // The flip side: a PC with no hardware encoder must not be congratulated on mirroring.
        // This is why the sentence is conditional rather than hard-coded.

        // Arrange: no capture backend, which blocks mirroring without being a network fault.
        var host = Build.Host() with { ScreenCaptureBackend = null };

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.SecondScreen].Reason.ShouldNotContain("already mirror");
    }

    [Fact]
    public void Assess_SecondScreen_OffersNoRemedyBecauseNoSettingWouldHelp()
    {
        // A remedy would send someone hunting for a driver that Flint has not shipped.

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        report[CastMode.SecondScreen].Remedy.ShouldBeNull();
    }

    [Fact]
    public void Assess_SecondScreen_StillInheritsARealMirrorBlocker()
    {
        // Separate flags must not mean separate hardware judgements: a PC that cannot mirror
        // cannot present a second screen either, and the reason it hears should be the actionable
        // hardware one rather than "not built yet".

        // Arrange
        var host = Build.Host([Build.SoftwareOnly()]);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.SecondScreen].Status.ShouldBe(ModeStatus.Blocked);
        report[CastMode.SecondScreen].Reason.ShouldContain("encoder");
    }

    [Fact]
    public void Assess_EncodersNotProbed_SaysSoRatherThanClaimingNoneExist()
    {
        // "Not asked" and "asked and found nothing" are different claims, and only one is true here.

        // Arrange
        var host = Build.Host([]) with { EncodersProbed = false };

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Reason.ShouldContain("has not probed");
        mirror.Reason.ShouldNotContain("No hardware video encoder was found");
    }

    [Fact]
    public void Assess_EncodersProbedAndEmpty_ClaimsNoneExist()
    {
        // Arrange
        var host = Build.Host([]) with { EncodersProbed = true };

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.Mirror].Reason.ShouldContain("No hardware video encoder was found");
    }

    [Fact]
    public void Assess_NoCaptureBackend_BlocksMirroringInsteadOfClaimingItWorks()
    {
        // The receiver can decode a mirror stream. If this build cannot produce one, saying the
        // mode is available would offer a control that fails the moment it is used.

        // Arrange
        var host = Build.Host(captureBackend: null);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Reason.ShouldContain("no screen-capture backend");
    }

    [Fact]
    public void Assess_NoCaptureBackend_AlsoBlocksSecondScreen()
    {
        // Second screen consumes captured frames too, so it cannot outlive the capture gate.

        // Arrange
        var host = Build.Host(captureBackend: null);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.SecondScreen].Status.ShouldBe(ModeStatus.Blocked);
    }

    [Fact]
    public void Assess_NoCaptureBackend_DoesNotBlockMediaHandoff()
    {
        // Playing a file needs no capture at all; blocking it here would be a false negative.

        // Arrange
        var host = Build.Host(captureBackend: null);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.MediaHandoff].Status.ShouldBe(ModeStatus.Available);
    }

    [Fact]
    public void Assess_NoHardwareEncoder_BlocksMirroringRatherThanFallingBackToSoftware()
    {
        // Arrange
        var host = Build.Host([Build.SoftwareOnly()]);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Reason.ShouldContain("Software encoding cannot meet the latency");
    }

    [Theory]
    [InlineData(45.0)]
    [InlineData(90.0)]
    public void Assess_RoundTripTooHigh_BlocksMirroringAndQuotesTheMeasurement(double roundTripMs)
    {
        // Arrange
        var path = Build.Path(roundTripMs: roundTripMs);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Reason.ShouldContain(roundTripMs.ToString("F0"));
        mirror.Reason.ShouldContain("round trip");
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(19.9)]
    public void Assess_ThroughputTooLow_BlocksMirroringAndQuotesTheMeasurement(double throughputMbps)
    {
        // Arrange
        var path = Build.Path(throughputMbps: throughputMbps);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Blocked);
        mirror.Reason.ShouldContain(throughputMbps.ToString("F1"));
    }

    [Fact]
    public void Assess_LatencyIsReportedBeforeThroughputWhenBothFail()
    {
        // Latency is the thing the user feels, and no amount of bandwidth fixes it, so it leads.

        // Arrange
        var path = Build.Path(roundTripMs: 90.0, throughputMbps: 5.0);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert
        report[CastMode.Mirror].Reason.ShouldContain("round trip");
    }

    [Fact]
    public void Assess_ThroughputNeverMeasured_DoesNotClaimThePathIsTooSlow()
    {
        // The TCP probe measures latency but not capacity. Reporting an unmeasured capacity as a
        // failing one would invent a blocker out of a gap in Flint's own knowledge.

        // Arrange
        var path = Build.Path(throughputMbps: -1.0) with { ThroughputMeasured = false };

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert: an unmeasured capacity is not a failing one, so the mode is still offered — but
        // the gap in Flint's knowledge is stated rather than papered over, and the sentinel value
        // standing in for "unmeasured" never reaches the user.
        var mirror = report[CastMode.Mirror];
        mirror.Status.ShouldBe(ModeStatus.Available);
        mirror.Reason.ShouldContain("Throughput has not been measured");
        mirror.Reason.ShouldNotContain("-1.0");
    }

    [Fact]
    public void Assess_ThroughputNeverMeasured_DoesNotBlockMediaHandoffEither()
    {
        // Arrange
        var path = Build.Path(throughputMbps: -1.0) with { ThroughputMeasured = false };

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert
        report[CastMode.MediaHandoff].Status.ShouldBe(ModeStatus.Available);
    }

    [Fact]
    public void Assess_UnmeasuredPath_DoesNotInventANetworkVerdict()
    {
        // A null path means "not measured", which must never be treated as "measured and bad".

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path: null);

        // Assert: no network-shaped verdict is invented out of a measurement never taken.
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Available);
    }

    [Fact]
    public void Assess_SecondScreenOnAffectedWindowsBuild_WarnsAboutThePrimaryDisplayLimitation()
    {
        // Arrange
        var host = Build.Host(windowsBuild: CapabilityAssessor.IddPrimaryDisplayBrokenFromBuild);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.SecondScreen].Reason.ShouldContain("cannot be made the primary display");
    }

    [Fact]
    public void Assess_SecondScreenOnOlderWindowsBuild_OmitsTheLimitationCaveat()
    {
        // Arrange
        var host = Build.Host(windowsBuild: CapabilityAssessor.IddPrimaryDisplayBrokenFromBuild - 1);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.SecondScreen].Reason.ShouldNotContain("primary display");
    }

    [Fact]
    public void Assess_SecondScreen_InheritsEveryMirrorBlocker()
    {
        // Arrange
        var host = Build.Host([Build.SoftwareOnly()]);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.SecondScreen].Status.ShouldBe(ModeStatus.Blocked);
        report[CastMode.SecondScreen].Mode.ShouldBe(CastMode.SecondScreen);
    }

    [Fact]
    public void Assess_MediaHandoff_DoesNotRequireAHardwareEncoder()
    {
        // Direct play never re-encodes, so an encoder-less host must not block it.

        // Arrange
        var host = Build.Host([Build.SoftwareOnly()]);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), host, Build.Path());

        // Assert
        report[CastMode.MediaHandoff].Status.ShouldBe(ModeStatus.Available);
    }

    [Fact]
    public void Assess_MediaHandoffOnSlowPath_BlocksBelowItsOwnLowerThreshold()
    {
        // Arrange
        var path = Build.Path(throughputMbps: CapabilityAssessor.MinimumMediaHandoffMbps - 1);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert
        report[CastMode.MediaHandoff].Status.ShouldBe(ModeStatus.Blocked);
    }

    [Fact]
    public void Assess_PathBetweenTheTwoThresholds_BlocksMirroringButNotMediaHandoff()
    {
        // Arrange
        var path = Build.Path(throughputMbps: 12.0);

        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), path);

        // Assert
        report[CastMode.Mirror].Status.ShouldBe(ModeStatus.Blocked);
        report[CastMode.MediaHandoff].Status.ShouldBe(ModeStatus.Available);
    }

    [Fact]
    public void Assess_AlwaysReturnsExactlyOneVerdictPerMode()
    {
        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        var modes = Enum.GetValues<CastMode>();
        report.Verdicts.Count.ShouldBe(modes.Length);
        report.Verdicts.Select(verdict => verdict.Mode).ShouldBeUnique();
    }

    [Fact]
    public void Assess_EveryVerdict_CarriesANonEmptyUserFacingReason()
    {
        // Act
        var report = CapabilityAssessor.Assess(Build.Device(), Build.Host(), Build.Path());

        // Assert
        report.Verdicts.ShouldAllBe(verdict => !string.IsNullOrWhiteSpace(verdict.Reason));
    }

    [Fact]
    public void Assess_ImpossibleVerdicts_NeverCarryARemedy()
    {
        // Arrange
        var device = Build.Device(FireTvPlatform.Vega, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report.Verdicts
            .Where(verdict => verdict.Status is ModeStatus.Impossible)
            .ShouldAllBe(verdict => verdict.Remedy == null);
    }

    [Fact]
    public void Assess_BlockedVerdicts_AlwaysCarryARemedy()
    {
        // A blocker the user cannot act on should have been Impossible instead.

        // Arrange
        var device = Build.Device(FireTvPlatform.Unknown, AdbConnectionState.Refused);

        // Act
        var report = CapabilityAssessor.Assess(device, Build.Host(), Build.Path());

        // Assert
        report.Verdicts
            .Where(verdict => verdict.Status is ModeStatus.Blocked)
            .ShouldAllBe(verdict => !string.IsNullOrWhiteSpace(verdict.Remedy));
    }

    [Fact]
    public void Assess_NullHost_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(
            () => CapabilityAssessor.Assess(Build.Device(), host: null!, Build.Path()));
    }

    [Fact]
    public void Assess_IsPure_ReturningEqualVerdictsForEqualInputs()
    {
        // Arrange
        var device = Build.Device();
        var host = Build.Host();
        var path = Build.Path();

        // Act
        var first = CapabilityAssessor.Assess(device, host, path);
        var second = CapabilityAssessor.Assess(device, host, path);

        // Assert
        first.Verdicts.ShouldBe(second.Verdicts);
    }
}
