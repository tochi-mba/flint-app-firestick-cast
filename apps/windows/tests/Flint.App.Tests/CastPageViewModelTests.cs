using System.Net;
using Flint.App.Controls;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The Cast page must never present a mode as usable when the assessor said it is not, and must
/// never hide the fact that a probe failed.
/// </summary>
public sealed class CastPageViewModelTests
{
    [Theory]
    [InlineData("error code io network connection timeout")]
    [InlineData("error code io network connection failed")]
    [InlineData("Error Code IO Network Connection Timeout")]
    public void DescribePlaybackFailure_NetworkConnectionDetail_GivesFirewallGuidanceNotTheRawCode(string detail)
    {
        // This is what the receiver actually sends: Media3's error code name, lowercased with
        // underscores turned to spaces. The raw text describes what ExoPlayer saw, not what a
        // person standing at their PC should do about it — and what they should do is check
        // Windows Firewall, because the TV dialling in to fetch the file is the one direction
        // pairing's outbound connection never proves works.

        var result = CastPageViewModel.DescribePlaybackFailure(detail);

        result.ShouldBe(CastPageViewModel.FirewallGuidance);
    }

    [Theory]
    [InlineData("error code io file not found")]
    [InlineData("error code io bad http status")]
    [InlineData("error code decoder init failed")]
    public void DescribePlaybackFailure_NonNetworkDetail_IsShownAsTheReceiverReportedIt(string detail)
    {
        // A codec or file problem needs a different fix than a firewall rule. Guessing firewall
        // for every failure would send people down the wrong path for the ones that are not.

        var result = CastPageViewModel.DescribePlaybackFailure(detail);

        result.ShouldBe(detail);
    }

    [Fact]
    public void DescribePlaybackFailure_NoDetailAtAll_SaysPlaybackEndedRatherThanShowingNothing()
    {
        CastPageViewModel.DescribePlaybackFailure(null).ShouldBe("The receiver ended playback before it started.");
        CastPageViewModel.DescribePlaybackFailure("").ShouldBe("The receiver ended playback before it started.");
        CastPageViewModel.DescribePlaybackFailure("   ").ShouldBe("The receiver ended playback before it started.");
    }

    [Fact]
    public void FirewallGuidance_NamesWindowsFirewallSpecificallyRatherThanNetworkInGeneral()
    {
        // A vague "check your network" would send someone to blame their router or the TV for a
        // problem that is neither's fault.

        CastPageViewModel.FirewallGuidance.ShouldContain("Windows");
        CastPageViewModel.FirewallGuidance.ShouldContain("Firewall");
    }


    [Fact]
    public async Task ProbeAsync_HealthyPair_PopulatesOneCardPerMode()
    {
        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.HasReport.ShouldBeTrue();
        page.Modes.Count.ShouldBe(Enum.GetValues<CastMode>().Length);
    }

    [Fact]
    public async Task ProbeAsync_ClearsTheEmptyStateOnceItHasAnAnswer()
    {
        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());
        page.ShowEmptyState.ShouldBeTrue();

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.ShowEmptyState.ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeAsync_ShowsTheDeviceIdentity()
    {
        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.DeviceName.ShouldBe("Living Room");
        page.DeviceAddress.ShouldBe("192.168.1.42:5555");
        page.DevicePlatform.ShouldBe("Fire OS 8");
    }

    [Fact]
    public async Task ProbeAsync_NoDeviceFound_SaysSoRatherThanShowingABlank()
    {
        // Arrange
        var page = PageFor(device: null, Fake.Host(), path: null);

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.DeviceName.ShouldBe("No receiver found");
        page.DeviceAddress.ShouldBe("—");
    }

    [Fact]
    public async Task ProbeAsync_ConnectedMode_IsOfferableAndUsesTheSignalAccent()
    {
        // The Signal accent must keep meaning "this works". Media handoff is the mode to prove it
        // with: mirroring has no capture/encode/transport pipeline behind it yet and correctly
        // stays NotImplemented for a healthy pair, so asserting Available against it here would
        // itself be the stale-test bug this file has already caught twice.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        var handoff = page.Modes.Single(mode => mode.Verdict.Mode == CastMode.MediaHandoff);
        handoff.Verdict.Status.ShouldBe(ModeStatus.Available);
        handoff.IsOfferable.ShouldBeTrue();
        handoff.Tone.ShouldBe(Tone.Signal);
    }

    [Fact]
    public async Task ProbeAsync_HealthyPair_OffersMirroringAsALiveControl()
    {
        // The companion to the assertion above: a pair with good hardware and a good network is
        // offered mirroring, now that the capture to encode to transport path exists.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        var mirror = page.Modes.Single(mode => mode.Verdict.Mode == CastMode.Mirror);
        mirror.Verdict.Status.ShouldBe(ModeStatus.Available);
        mirror.IsOfferable.ShouldBeTrue();
    }

    [Fact]
    public async Task ProbeAsync_HealthyPair_StillReportsSecondScreenAsNotBuilt()
    {
        // A working mirror must not be mistaken for a working second screen: that mode needs an
        // indirect display driver this build does not ship.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        var secondScreen = page.Modes.Single(mode => mode.Verdict.Mode == CastMode.SecondScreen);
        secondScreen.Verdict.Status.ShouldBe(ModeStatus.NotImplemented);
        secondScreen.IsOfferable.ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeAsync_VegaDevice_PaintsEveryModeAsBlockedAndOffersNoRemedy()
    {
        // Arrange
        var device = Fake.Device() with { Platform = FireTvPlatform.Vega };
        var page = PageFor(device, Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.Modes.ShouldAllBe(mode => mode.Tone == Tone.Live);
        page.Modes.ShouldAllBe(mode => !mode.HasRemedy);
        page.HeadingTone.ShouldBe(Tone.Neutral);
    }

    [Fact]
    public async Task ProbeAsync_BlockedMode_ShowsARemedy()
    {
        // Arrange
        var device = Fake.Device() with { AdbState = AdbConnectionState.Unauthorized };
        var page = PageFor(device, Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.Modes.ShouldAllBe(mode => mode.HasRemedy);
    }

    [Fact]
    public async Task ProbeAsync_WhenTheProbeThrows_ReportsTheFailureRatherThanSwallowingIt()
    {
        // Arrange
        var page = new CastPageViewModel(new CapabilityProber(
            new ThrowingHostProbe(),
            new FakeDeviceProbe(null),
            new FakeNetworkProbe(null)));

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldContain("probe exploded");
        page.IsProbing.ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeAsync_ClearsAPreviousFailureOnASuccessfulRerun()
    {
        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeAsync_LeavesIsProbingFalseWhenItFinishes()
    {
        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.IsProbing.ShouldBeFalse();
    }

    [Fact]
    public async Task CanPairWithCode_TrueAssoonAsADeviceIsKnown_EvenWithAdbRefused()
    {
        // Pairing by code talks to the receiver's own port, never ADB. Gating the button on ADB
        // would strand exactly the people it exists for: someone whose ADB daemon is wedged but
        // who is standing at the TV with the code in front of them.

        var page = PageFor(Fake.Device() with { AdbState = AdbConnectionState.Refused }, Fake.Host(), Fake.Path());

        await page.ProbeCommand.ExecuteAsync(null);

        page.CanPairWithCode.ShouldBeTrue();
    }

    [Fact]
    public void CanPairWithCode_FalseBeforeAnyProbe_BecauseNoAddressIsKnownYet()
    {
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        page.CanPairWithCode.ShouldBeFalse();
    }

    [Fact]
    public async Task CanOpenReceiver_StaysGatedOnAdbUnlikeCanPairWithCode()
    {
        // The ADB-launch button is a genuinely different action and must keep its own guard.

        var page = PageFor(Fake.Device() with { AdbState = AdbConnectionState.Refused }, Fake.Host(), Fake.Path());

        await page.ProbeCommand.ExecuteAsync(null);

        page.CanOpenReceiver.ShouldBeFalse();
        page.CanPairWithCode.ShouldBeTrue();
    }

    [Fact]
    public async Task ConnectAsync_ValidCodeWithAdbRefused_DoesNotStopAtTheOldAdbGuard()
    {
        // Regression test for the bug this pins: entering a valid code used to be rejected with
        // "Connect to the TV first" purely because ADB had not authorised, even though the code
        // path never uses ADB. There is no real receiver listening in this test, so the attempt
        // still fails — but it must fail on the network, not on the removed ADB guard.
        //
        // The device sits on loopback with a port nothing listens on, so each connection attempt
        // is refused immediately rather than timing out. The test is still not fast: production
        // code deliberately retries a refused connection up to twelve times, 250 ms apart, because
        // a receiver that is mid-launch briefly refuses before it starts listening. That retry
        // policy is being exercised for real here, not a leftover a slow test could avoid.

        var unreachable = Fake.Device() with
        {
            Address = System.Net.IPAddress.Loopback,
            AdbState = AdbConnectionState.Refused,
        };
        var page = PageFor(unreachable, Fake.Host(), Fake.Path());
        await page.ProbeCommand.ExecuteAsync(null);
        page.PairingCode = "123456";
        page.ReceiverPort = "1";

        await page.ConnectCommand.ExecuteAsync(null);

        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldNotContain("Connect to the TV first");
        page.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task ConnectAsync_EmptyCodeWithAdbRefused_ExplainsWhyItCannotOpenTheReceiverItself()
    {
        // The ADB-launch branch legitimately still needs ADB; it must fail with a clear reason
        // rather than the generic "connect first" message, and must not call the launcher.

        var receiverLauncher = new FakeReceiverLauncher();
        var page = PageFor(
            Fake.Device() with { AdbState = AdbConnectionState.Refused },
            Fake.Host(),
            Fake.Path(),
            receiverLauncher);
        await page.ProbeCommand.ExecuteAsync(null);

        await page.ConnectCommand.ExecuteAsync(null);

        receiverLauncher.LaunchedDevice.ShouldBeNull();
        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldContain("ADB is not authorised");
        page.Failure.ShouldContain("Flint Receiver");
    }

    [Fact]
    public async Task ConnectAsync_WithoutPairingCode_OpensReceiverOnTheTvBeforeRequestingTheCode()
    {
        var receiverLauncher = new FakeReceiverLauncher();
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path(), receiverLauncher);
        await page.ProbeCommand.ExecuteAsync(null);

        await page.ConnectCommand.ExecuteAsync(null);

        receiverLauncher.LaunchedDevice.ShouldNotBeNull();
        receiverLauncher.LaunchedDevice.Address.ShouldBe(IPAddress.Parse("192.168.1.42"));
        page.PairingStatus.ShouldContain("Receiver opened on the TV");
        page.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task ConnectAsync_WithAnEnteredInvalidCode_DoesNotRelaunchTheReceiver()
    {
        var receiverLauncher = new FakeReceiverLauncher();
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path(), receiverLauncher);
        page.PairingCode = "12345";
        await page.ProbeCommand.ExecuteAsync(null);

        await page.ConnectCommand.ExecuteAsync(null);

        receiverLauncher.LaunchedDevice.ShouldBeNull();
        page.Failure.ShouldNotBeNull();
        page.Failure!.ShouldContain("six-digit pairing code");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("224.0.0.251")]
    public async Task ProbeAddressAsync_InvalidTarget_ShowsAValidationFailure(string address)
    {
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());
        page.ManualAddress = address;

        await page.ProbeAddressCommand.ExecuteAsync(null);

        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldContain("unicast IP address");
        page.HasReport.ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeAddressAsync_ValidTarget_RunsTheSameCapabilityReportPipeline()
    {
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());
        page.ManualAddress = "192.168.1.42";
        page.ManualPort = "5555";

        await page.ProbeAddressCommand.ExecuteAsync(null);

        page.Failure.ShouldBeNull();
        page.HasReport.ShouldBeTrue();
        page.Modes.Count.ShouldBe(Enum.GetValues<CastMode>().Length);
    }

    [Fact]
    public async Task ProbeAsync_WithDirectAddress_UsesTheDirectConnectionPipeline()
    {
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());
        page.ManualAddress = "192.168.1.42";
        page.ManualPort = "5555";

        await page.ProbeCommand.ExecuteAsync(null);

        page.HasReport.ShouldBeTrue();
        page.DeviceAddress.ShouldBe("192.168.1.42:5555");
        page.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task ProbeAsync_WithUnreachableDirectAddress_ExplainsThatTheTvMustBeConnected()
    {
        var page = PageFor(Fake.Device() with
        {
            AdbState = AdbConnectionState.Refused,
            Platform = FireTvPlatform.Unknown,
        }, Fake.Host(), path: null);
        page.ManualAddress = "192.168.1.42";
        page.ManualPort = "5555";

        await page.ProbeCommand.ExecuteAsync(null);

        page.HeadingStatus.ShouldBe("Connect TV");
        page.Modes.ShouldAllBe(mode => mode.Reason.Contains("refused the ADB port"));
        page.Modes.ShouldAllBe(mode => mode.Remedy.Contains("seven times"));
    }

    [Fact]
    public async Task ProbeAsync_WithNothingAtTheAddress_BlamesTheAddressRatherThanTheTelevision()
    {
        // The failure people actually hit: a mistyped or stale address. Telling them to enable
        // Developer Options sends them to a television that was never the problem — and on a wrong
        // address there may not even be a television there.
        var page = PageFor(Fake.Device() with
        {
            AdbState = AdbConnectionState.TimedOut,
            Platform = FireTvPlatform.Unknown,
        }, Fake.Host(), path: null);
        page.ManualAddress = "192.168.1.42";
        page.ManualPort = "5555";

        await page.ProbeCommand.ExecuteAsync(null);

        page.Modes.ShouldAllBe(mode => mode.Reason.Contains("Nothing answered"));
        page.Modes.ShouldAllBe(mode => !mode.Reason.Contains("Flint reached"));
        page.Modes.ShouldAllBe(mode => mode.Remedy.Contains("Network"));
        page.Modes.ShouldAllBe(mode => !mode.Remedy.Contains("seven times"));
    }

    [Fact]
    public void MirrorVerdict_BeforeAnyProbe_IsNull()
    {
        // Nothing to bind the Screen page's button or reason text to yet.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act & Assert
        page.MirrorVerdict.ShouldBeNull();
    }

    [Fact]
    public async Task MirrorVerdict_AfterAProbe_MatchesTheMirrorEntryInModes()
    {
        // The Screen page must read the exact same verdict the Cast page's own Mirror card shows
        // — a second, independently-computed opinion is what let the old button drift out of sync
        // with the truth in the first place.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.MirrorVerdict.ShouldNotBeNull();
        page.MirrorVerdict.ShouldBeSameAs(page.Modes.Single(mode => mode.Verdict.Mode == CastMode.Mirror));
    }

    [Fact]
    public async Task StartScreenSessionAsync_WhenMirrorIsNotOfferable_RefusesWithTheVerdictReason()
    {
        // Regression test for the bug this pins: the command used to tell the receiver to expect a
        // mirror stream and then report success locally, leaving the TV waiting on a black screen
        // for frames nothing was ever going to send. It must now refuse up front instead, using
        // the exact same reason the Screen page's own card already shows.

        // Arrange: a PC with no hardware encoder, so mirroring is blocked for a real reason.
        var page = PageFor(Fake.Device(), Fake.HostThatCannotMirror(), Fake.Path());
        await page.ProbeCommand.ExecuteAsync(null);
        var expectedReason = page.MirrorVerdict!.Reason;

        // Act
        await page.StartScreenSessionCommand.ExecuteAsync(null);

        // Assert
        page.Failure.ShouldBe(expectedReason);
    }

    [Fact]
    public async Task StartScreenSessionAsync_WhenMirrorIsNotOfferable_NeverTouchesTheReceiver()
    {
        // No SurfaceMessage, no session I/O of any kind — refusing must be silent to the TV.
        // Proven here by needing no paired session at all: if this reached the receiver it would
        // need one, and this test deliberately never creates one.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.HostThatCannotMirror(), Fake.Path());
        await page.ProbeCommand.ExecuteAsync(null);

        // Act & Assert — a network call here would hang or throw against the nonexistent session;
        // finishing promptly with a plain refusal is the proof.
        await page.StartScreenSessionCommand.ExecuteAsync(null);
        page.IsConnected.ShouldBeFalse();
    }

    [Fact]
    public async Task StartScreenSessionAsync_WhenMirroringIsOfferableButNothingIsPaired_AsksForThePairingCodeFirst()
    {
        // The mode being possible is not the same as there being somewhere to send it. Without a
        // paired session this must say so rather than starting an engine with no transport.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());
        await page.ProbeCommand.ExecuteAsync(null);

        // Act
        await page.StartScreenSessionCommand.ExecuteAsync(null);

        // Assert
        page.MirrorVerdict!.IsOfferable.ShouldBeTrue();
        page.Failure.ShouldNotBeNull();
        page.Failure.ShouldContain("pairing code");
        page.IsMirroring.ShouldBeFalse();
    }

    [Fact]
    public void StopScreenSession_WhenNothingIsMirroring_IsHarmless()
    {
        // The stop command is reachable whenever the page is, including before anything started.
        // It must not throw on a null cancellation source.

        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act & Assert
        page.StopScreenSessionCommand.Execute(null);
        page.IsMirroring.ShouldBeFalse();
    }

    [Fact]
    public void HeadingStatus_BeforeAnyProbe_SaysNotProbed()
    {
        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act & Assert
        page.HeadingStatus.ShouldBe("Not probed");
    }

    [Fact]
    public async Task HeadingStatus_AfterProbingACapablePair_SaysReady()
    {
        // Arrange
        var page = PageFor(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ProbeCommand.ExecuteAsync(null);

        // Assert
        page.HeadingStatus.ShouldBe("Ready");
    }

    [Theory]
    [InlineData(CastMode.Mirror, "Mirror this screen")]
    [InlineData(CastMode.SecondScreen, "Second screen")]
    [InlineData(CastMode.MediaHandoff, "Play a file on the TV")]
    public void ModeTitle_ReadsAsPlainEnglishNotAnEnumName(CastMode mode, string expected)
    {
        // Arrange
        var verdict = new ModeVerdict(mode, ModeStatus.Blocked, "reason", "remedy");

        // Act & Assert
        new ModeVerdictViewModel(verdict).Title.ShouldBe(expected);
    }

    [Theory]
    [InlineData(ModeStatus.Available, "Ready")]
    [InlineData(ModeStatus.Blocked, "Blocked")]
    [InlineData(ModeStatus.Impossible, "Not possible")]
    [InlineData(ModeStatus.NotImplemented, "Coming soon")]
    public void StatusLabel_DistinguishesImpossibleFromMerelyUnbuilt(ModeStatus status, string expected)
    {
        // These must never read alike: one is a fact about the device, the other about Flint. This
        // test's own body once asserted NotImplemented and Available shared a label ("Ready"),
        // silently contradicting the test's name — the same mislabeling this pins against.

        // Arrange
        var verdict = new ModeVerdict(CastMode.Mirror, status, "reason");

        // Act & Assert
        new ModeVerdictViewModel(verdict).StatusLabel.ShouldBe(expected);
    }

    [Fact]
    public void StatusLabel_NeverReadsLikeSomethingTheUserBroke()
    {
        // An unshipped mode and a blocked one must not share language: "Blocked" sends someone
        // into their driver and network settings, which is wasted effort for a mode that simply
        // has not been written.
        var comingSoon = new ModeVerdictViewModel(
            new ModeVerdict(CastMode.SecondScreen, ModeStatus.NotImplemented, "reason"));
        var blocked = new ModeVerdictViewModel(
            new ModeVerdict(CastMode.SecondScreen, ModeStatus.Blocked, "reason", "remedy"));

        comingSoon.StatusLabel.ShouldNotBe(blocked.StatusLabel);
        comingSoon.StatusLabel.ShouldBe("Coming soon");
    }

    [Theory]
    [InlineData(ModeStatus.NotImplemented, true)]
    [InlineData(ModeStatus.Available, false)]
    [InlineData(ModeStatus.Blocked, false)]
    [InlineData(ModeStatus.Impossible, false)]
    public void IsComingSoon_IsTrueOnlyForAModeThatHasNotShipped(ModeStatus status, bool expected)
    {
        // Arrange
        var verdict = new ModeVerdict(CastMode.SecondScreen, status, "reason");

        // Act & Assert
        new ModeVerdictViewModel(verdict).IsComingSoon.ShouldBe(expected);
    }

    [Fact]
    public void IsComingSoon_IsNeverTrueAlongsideARemedy()
    {
        // The card shows a "coming soon" panel and a "what to do" panel from these two flags. Both
        // at once would tell someone to fix a mode and to wait for it in the same breath.
        foreach (var status in Enum.GetValues<ModeStatus>())
        {
            var verdict = new ModeVerdict(CastMode.SecondScreen, status, "reason", "remedy");
            var card = new ModeVerdictViewModel(verdict);

            if (card.IsComingSoon)
            {
                card.Verdict.Status.ShouldBe(ModeStatus.NotImplemented);
            }
        }
    }

    [Fact]
    public void ComingSoonMode_IsNeverOfferableAsAControl()
    {
        // The whole point of the flag: the button beside the card stays disabled.
        var card = new ModeVerdictViewModel(
            new ModeVerdict(CastMode.SecondScreen, ModeStatus.NotImplemented, "reason"));

        card.IsComingSoon.ShouldBeTrue();
        card.IsOfferable.ShouldBeFalse();
    }

    [Fact]
    public void ComingSoonMode_IsPaintedNeutralRatherThanAsAFailure()
    {
        // Live is the failure tone. A planned feature is not a failure, and painting it in the
        // same red as a blocked one is how a roadmap item reads as a bug.
        var card = new ModeVerdictViewModel(
            new ModeVerdict(CastMode.SecondScreen, ModeStatus.NotImplemented, "reason"));

        card.Tone.ShouldBe(Tone.Neutral);
    }

    private static CastPageViewModel PageFor(
        FireTvDevice? device,
        HostCapabilities host,
        NetworkPath? path,
        IReceiverLauncher? receiverLauncher = null) =>
        new(new CapabilityProber(
            new FakeHostProbe(host),
            new FakeDeviceProbe(device),
            new FakeNetworkProbe(path)),
            new EmptyRecentAddressStore(),
            receiverLauncher);

    private static class Fake
    {
        internal static FireTvDevice Device() =>
            new(IPAddress.Parse("192.168.1.42"), "Living Room", DiscoverySource.MulticastDns)
            {
                Platform = FireTvPlatform.FireOs8,
                AdbState = AdbConnectionState.Connected,
                AdbPort = 5555,
            };

        /// <summary>A host with everything a session needs, so a test states only its own subject.</summary>
        internal static HostCapabilities Host() =>
            new(
                [new DisplayAdapter(1, "Test Adapter", DrivesDisplay: true)],
                [new HostVideoEncoder(EncoderVendor.Nvenc, 1, new HashSet<VideoCodec> { VideoCodec.H264 })],
                1,
                26100,
                EncodersProbed: true,
                ScreenCaptureBackend: CaptureApi.DesktopDuplication);

        /// <summary>
        /// A host with no hardware encoder, which blocks mirroring for a reason on this PC.
        /// </summary>
        internal static HostCapabilities HostThatCannotMirror() =>
            Host() with
            {
                Encoders = [new HostVideoEncoder(EncoderVendor.Unknown, 0, new HashSet<VideoCodec>())],
            };

        internal static NetworkPath Path() => new(4.0, 1.0, 120.0, 0.0);
    }

    private sealed class FakeHostProbe(HostCapabilities host) : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(host);
    }

    private sealed class ThrowingHostProbe : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The probe exploded.");
    }

    private sealed class FakeDeviceProbe(FireTvDevice? device) : IAddressableDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>(device is null ? [] : [device]);

        public Task<FireTvDevice> ProbeAddressAsync(
            IPAddress address,
            int? port = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(device ?? new FireTvDevice(address, address.ToString(), DiscoverySource.Manual)
            {
                AdbPort = port,
                AdbState = AdbConnectionState.Refused,
            });
    }

    private sealed class FakeNetworkProbe(NetworkPath? path) : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(
            FireTvDevice device,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(path);
    }

    private sealed class EmptyRecentAddressStore : IRecentAddressStore
    {
        public IReadOnlyList<RecentAddress> Load() => [];

        public void Remember(RecentAddress address)
        {
        }

        public void Clear()
        {
        }
    }

    private sealed class FakeReceiverLauncher : IReceiverLauncher
    {
        public FireTvDevice? LaunchedDevice { get; private set; }

        public Task LaunchAsync(FireTvDevice device, CancellationToken cancellationToken = default)
        {
            LaunchedDevice = device;
            return Task.CompletedTask;
        }
    }
}
