using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.Session.Browser;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Reaching a trusted browser session, and refusing to pretend when it cannot be reached.
/// </summary>
/// <remarks>
/// Verification is the one deliberate human step in this feature: the fingerprint on the television
/// and the fingerprint on the desktop are compared by a person, and pressing Verify is what says
/// they matched. Everything past this point trusts that decision, so these tests care as much about
/// the refusals as about the success.
/// </remarks>
public sealed class BrowserPageViewModelVerifyTests
{
    [Fact]
    public async Task Verify_WithAdvertisedEndpointAndPairingCode_ReachesSecureReady()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);
        shell.Cast.PairingCode = "123456";

        var connector = new RecordingBrowserSessionConnector(new RecordingBrowserRemote());
        using var browser = new BrowserPageViewModel(shell.Cast, connector, new InMemoryBrowserTrustStore());

        await browser.VerifySecureReceiverCommand.ExecuteAsync(null);

        browser.SessionPhase.ShouldBe(BrowserUiPhase.SecureReady);
        browser.CanNavigate.ShouldBeTrue();
        browser.FingerprintDisplay.ShouldBe("0001-0203-0405");
        connector.ConnectCount.ShouldBe(1);
    }

    [Fact]
    public async Task Verify_WithoutPairingCode_ReportsMismatch()
    {
        // The pairing code is what proves the two ends are talking about the same television. With
        // no code there is nothing to compare, so the only honest outcome is a refusal — and no
        // connection attempt at all, which is what ConnectCount asserts.
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);

        var connector = new RecordingBrowserSessionConnector(new RecordingBrowserRemote());
        using var browser = new BrowserPageViewModel(shell.Cast, connector, new InMemoryBrowserTrustStore());

        await browser.VerifySecureReceiverCommand.ExecuteAsync(null);

        browser.SessionPhase.ShouldBe(BrowserUiPhase.Mismatch);
        browser.CanNavigate.ShouldBeFalse();
        connector.ConnectCount.ShouldBe(0);
        browser.LastError.ShouldNotBeNull();
        browser.LastError.ShouldContain("pairing code");
    }

    [Fact]
    public async Task ReturningPin_WithPairingCode_AutoReconnectsWithoutFirstUsePrompt()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);

        var trust = new InMemoryBrowserTrustStore();
        var endpoint = BrowserEndpointResolver.TryResolve(shell.Cast.Report!.Device!, null)!;
        var identity = BrowserFixtures.Identity(endpoint);
        await trust.SaveAsync(
            new BrowserTrustedReceiver(endpoint.ReceiverIdentity, identity.Fingerprint, DateTimeOffset.UtcNow));

        var connector = new RecordingBrowserSessionConnector(new RecordingBrowserRemote());
        using var browser = new BrowserPageViewModel(shell.Cast, connector, trust);

        shell.Cast.PairingCode = "123456";
        await browser.RefreshRememberedTrustAndMaybeReconnectForTestsAsync();

        browser.HasRememberedTrust.ShouldBeTrue();
        browser.SecureReceiverActionLabel.ShouldBe("CONNECT SECURE BROWSER");
        browser.SessionPhase.ShouldBe(BrowserUiPhase.SecureReady);
        browser.CanNavigate.ShouldBeTrue();
        connector.ConnectCount.ShouldBe(1);
    }

    [Fact]
    public async Task WithoutRememberedPin_DoesNotAutoConnectWhenPairingCodeArrives()
    {
        var shell = MainWindowViewModel.CreateWith(BrowserFixtures.Prober(BrowserFixtures.EligibleDevice()));
        await shell.Cast.ProbeCommand.ExecuteAsync(null);

        var connector = new RecordingBrowserSessionConnector(new RecordingBrowserRemote());
        using var browser = new BrowserPageViewModel(shell.Cast, connector, new InMemoryBrowserTrustStore());

        shell.Cast.PairingCode = "123456";
        await browser.RefreshRememberedTrustAndMaybeReconnectForTestsAsync();

        browser.HasRememberedTrust.ShouldBeFalse();
        browser.SecureReceiverActionLabel.ShouldBe("VERIFY SECURE RECEIVER");
        browser.SessionPhase.ShouldBe(BrowserUiPhase.Idle);
        connector.ConnectCount.ShouldBe(0);
    }
}
