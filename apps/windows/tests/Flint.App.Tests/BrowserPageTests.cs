using System.Net;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Flint.Discovery;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// Pins the first browser experience to an honest, inert capability presentation. A browser page
/// must never render a convincing address bar before the selected receiver has earned that state.
/// </summary>
public sealed class BrowserPageTests
{
    [AvaloniaTheory]
    [InlineData(780, 560)]
    [InlineData(1280, 1100)]
    public async Task RemoteTextEntryAndButtonsStayInsideThePage(int width, int height)
    {
        var model = await BrowserFixtures.ReadyViewModelAsync(new RecordingBrowserRemote());
        var page = new BrowserPage { DataContext = model };
        var window = new Window { Width = width, Height = height, Content = page };
        try
        {
            window.Show();
            window.UpdateLayout();
            foreach (var name in new[] { "Text to send to the TV", "Send text to TV", "Page up", "Escape", "Keyboard forwarding", "Web help" })
            {
                var control = page.GetVisualDescendants().OfType<Control>()
                    .First(item => AutomationProperties.GetName(item) == name);
                control.IsEffectivelyVisible.ShouldBeTrue(name);
                var origin = control.TranslatePoint(default, page)!.Value;
                origin.X.ShouldBeGreaterThanOrEqualTo(0, name);
                origin.Y.ShouldBeGreaterThanOrEqualTo(0, name);
                (origin.X + control.Bounds.Width).ShouldBeLessThanOrEqualTo(page.Bounds.Width + 1, name);
                (origin.Y + control.Bounds.Height).ShouldBeLessThanOrEqualTo(page.Bounds.Height + 1, name);
                if (name == "Text to send to the TV") control.Bounds.Width.ShouldBeGreaterThan(160);
            }
        }
        finally { window.Close(); model.Dispose(); }
    }

    [Fact]
    public void Shell_ProvidesAConnectedWebDestination()
    {
        var shell = MainWindowViewModel.CreateWith(Prober(DeviceWithoutBrowserEvidence()));

        shell.Destinations.Single(destination => destination.Label == "Web").IsImplemented.ShouldBeTrue();
        shell.Selected = shell.Destinations.Single(destination => destination.Label == "Web");

        shell.IsWebSelected.ShouldBeTrue();
        shell.IsPlaceholderSelected.ShouldBeFalse();
        shell.PrivacyLabel.ShouldContain("TV");
        shell.PrivacyLabel.ShouldContain("NO FLINT CLOUD");
    }

    [AvaloniaFact]
    public async Task Page_PendingEvidenceShowsExactReasonAndNoBrowserControls()
    {
        var shell = MainWindowViewModel.CreateWith(Prober(DeviceWithoutBrowserEvidence()));
        var page = new BrowserPage { DataContext = shell.Browser };
        var window = new Window { Width = 900, Height = 700, Content = page };
        try
        {
            window.Show();
            await shell.Cast.ProbeCommand.ExecuteAsync(null);
            window.UpdateLayout();

            Texts(page).ShouldContain(shell.Browser.Reason);
            Texts(page).ShouldContain(shell.Browser.Remedy!);
            shell.Browser.ShowSecureReceiverCard.ShouldBeTrue();
            VisibleTextBoxes(page).Count().ShouldBe(1);
            page.GetVisualDescendants().OfType<Button>().ShouldContain(button =>
                AutomationProperties.GetName(button) == "Verify or reconnect secure receiver");
            shell.Browser.CanNavigate.ShouldBeFalse();
            shell.Browser.CanVerify.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Page_EligibleEvidenceDoesNotPretendANavigationSessionAlreadyExists()
    {
        var shell = MainWindowViewModel.CreateWith(Prober(DeviceWithEligibleBrowserEvidence()));
        var page = new BrowserPage { DataContext = shell.Browser };
        var window = new Window { Width = 900, Height = 700, Content = page };
        try
        {
            window.Show();
            await shell.Cast.ProbeCommand.ExecuteAsync(null);
            window.UpdateLayout();

            shell.Browser.IsEligible.ShouldBeTrue();
            Texts(page).ShouldContain("ELIGIBLE");
            Texts(page).Any(text => text.Contains("TV connects directly to opened sites", StringComparison.Ordinal)).ShouldBeTrue();
            // Discovery advertised the port, so the port box is not offered: a filled field nobody
            // should touch reads as one more step before a session that is already reachable.
            shell.Browser.ShowBrowserPortEntry.ShouldBeFalse();
            VisibleTextBoxes(page).Count().ShouldBe(0);
            // Asserted by accessible name rather than by the visible label. REX primary actions are
            // rendered upper case with tracking, so the label is a presentation detail that changes
            // with the design system; the name a screen reader announces is the contract.
            page.GetVisualDescendants().OfType<Button>().ShouldContain(button =>
                AutomationProperties.GetName(button) == "Verify or reconnect secure receiver");
            shell.Browser.CanNavigate.ShouldBeFalse();
            // Eligible but no Cast pairing code yet — Verify stays off until the code is entered.
            shell.Browser.CanVerify.ShouldBeFalse();
            shell.Cast.PairingCode = "123456";
            shell.Browser.CanVerify.ShouldBeTrue();
            shell.Browser.ManualBrowserPort.ShouldBe("8443");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MainWindow_SelectingWebShowsTheDedicatedPage()
    {
        var shell = MainWindowViewModel.CreateWith(Prober(DeviceWithoutBrowserEvidence()));
        var window = new MainWindow { DataContext = shell };
        try
        {
            window.Show();
            shell.Selected = shell.Destinations.Single(destination => destination.Label == "Web");
            window.UpdateLayout();

            window.GetVisualDescendants().OfType<BrowserPage>().Single().IsVisible.ShouldBeTrue();
            window.GetVisualDescendants().OfType<CastPage>().Single().IsVisible.ShouldBeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    private static List<string> Texts(Control root) =>
    [
        .. root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible)
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!),
    ];

    /// <summary>
    /// The HUD stage stays in the tree while hidden; only effectively visible boxes count for the
    /// pre-verify "no navigation chrome" contract.
    /// </summary>
    private static IEnumerable<TextBox> VisibleTextBoxes(Control root) =>
        root.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible);

    private static FireTvDevice DeviceWithoutBrowserEvidence() =>
        DeviceWithEligibleBrowserEvidence() with { BrowserEvidence = null };

    private static FireTvDevice DeviceWithEligibleBrowserEvidence() =>
        new(IPAddress.Parse("192.168.1.42"), "Living Room", DiscoverySource.MulticastDns)
        {
            Platform = FireTvPlatform.FireOs8,
            AdbState = AdbConnectionState.Connected,
            AdbPort = 5555,
            BrowserEvidence = new BrowserReceiverEvidence(2, true, BrowserWebViewProbe.Passed)
            {
                SecureEndpointPort = 8443,
            },
        };

    private static CapabilityProber Prober(FireTvDevice device) => new(
        new FixedHostProbe(),
        new FixedDeviceProbe(device),
        new FixedNetworkProbe());

    private sealed class FixedHostProbe : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HostCapabilities(
                [new DisplayAdapter(1, "Test GPU", DrivesDisplay: true)],
                [new HostVideoEncoder(EncoderVendor.Nvenc, 1, new HashSet<VideoCodec> { VideoCodec.H264 })],
                1,
                26100,
                EncodersProbed: true,
                ScreenCaptureBackend: CaptureApi.DesktopDuplication));
    }

    private sealed class FixedDeviceProbe(FireTvDevice device) : IDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>([device]);
    }

    private sealed class FixedNetworkProbe : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(FireTvDevice device, CancellationToken cancellationToken = default) =>
            Task.FromResult<NetworkPath?>(new NetworkPath(4, 1, 120, 0));
    }
}
