using System.Linq;
using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.Services;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Flint.Discovery;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// How the Cast page presents a mode that has not shipped.
/// </summary>
/// <remarks>
/// Rendered rather than asserted on the view model alone. The view model tests prove the flags are
/// right; only the real template proves the bindings that read them are wired to the panels they
/// are supposed to control, which is where a "coming soon" note that never appears would hide.
/// </remarks>
public sealed class CastPageComingSoonTests
{
    [AvaloniaFact]
    public async Task SecondScreenCard_AnnouncesItselfAsComingSoon()
    {
        // The pill's template upper-cases its text, so the rendered word is not the view model's.

        // Arrange
        var page = Render(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert
        page.ViewModel.Modes
            .Single(mode => mode.Verdict.Mode == CastMode.SecondScreen)
            .StatusLabel.ShouldBe("Coming soon");
        Texts(page.View).ShouldContain("COMING SOON");
    }

    [AvaloniaFact]
    public async Task SecondScreenCard_ShowsTheComingSoonPanelRatherThanAWhatToDoPanel()
    {
        // The distinction the whole change exists for: an unshipped mode must not hand out
        // instructions, because following them cannot possibly help.

        // Arrange
        var page = Render(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert
        var texts = Texts(page.View);
        texts.ShouldContain("NO ACTION NEEDED");
        texts.ShouldContain(text => text.Contains("Nothing to change on your PC"));
        texts.ShouldNotContain("WHAT TO DO");
    }

    [AvaloniaFact]
    public async Task SecondScreenCard_SaysWhatTheModeWouldDo()
    {
        // A status with no explanation of the feature leaves the reader guessing what they are
        // waiting for.

        // Arrange
        var page = Render(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert
        Texts(page.View).ShouldContain(text => text.Contains("extra desktop"));
    }

    [AvaloniaFact]
    public async Task AWorkingMode_ShowsNoComingSoonPanelAtAll()
    {
        // The panel is bound to a per-card flag, so a page containing one unshipped mode must not
        // paint the note onto the modes that do work. Media handoff is available on this pair.

        // Arrange
        var page = Render(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert: exactly one card is unshipped, so the note appears exactly once.
        var comingSoonCards = page.ViewModel.Modes.Count(mode => mode.IsComingSoon);
        comingSoonCards.ShouldBe(1);
        Texts(page.View).Count(text => text == "NO ACTION NEEDED").ShouldBe(1);
    }

    [AvaloniaFact]
    public async Task ABlockedMode_ShowsAWhatToDoPanelAndNoComingSoonPanel()
    {
        // The mirror image of the test above: a mode someone can fix keeps its instructions, and
        // must not be softened into a roadmap item.

        // Arrange: ADB refused, which blocks every mode with an actionable reason.
        var device = Fake.Device() with { AdbState = AdbConnectionState.Refused };
        var page = Render(device, Fake.Host(), Fake.Path());

        // Act
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert
        var texts = Texts(page.View);
        texts.ShouldContain("WHAT TO DO");
        texts.ShouldNotContain("NO ACTION NEEDED");
        page.ViewModel.Modes.ShouldAllBe(mode => !mode.IsComingSoon);
    }

    /// <summary>
    /// The text a person would actually read on the page.
    /// </summary>
    /// <remarks>
    /// Filtered by <see cref="Visual.IsEffectivelyVisible"/>, which is the whole point of this
    /// helper. A control hidden by <c>IsVisible="false"</c> stays in the visual tree in Avalonia,
    /// so collecting every descendant reports hidden panels as though they were on screen — and an
    /// assertion that a panel is absent would pass no matter what the binding did.
    /// </remarks>
    private static List<string> Texts(Control root) =>
    [
        .. root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible)
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!),
    ];

    private static (CastPage View, CastPageViewModel ViewModel) Render(
        FireTvDevice? device,
        HostCapabilities host,
        NetworkPath? path)
    {
        var viewModel = new CastPageViewModel(
            new CapabilityProber(
                new FakeHostProbe(host),
                new FakeDeviceProbe(device),
                new FakeNetworkProbe(path)),
            new EmptyRecentAddressStore());
        var view = new CastPage { DataContext = viewModel };
        var window = new Window { Width = 1180, Height = 900, Content = view };
        window.Show();
        window.UpdateLayout();
        return (view, viewModel);
    }

    private static class Fake
    {
        internal static FireTvDevice Device() =>
            new(IPAddress.Parse("192.168.1.42"), "Living Room", DiscoverySource.MulticastDns)
            {
                Platform = FireTvPlatform.FireOs8,
                AdbState = AdbConnectionState.Connected,
                AdbPort = 5555,
            };

        internal static HostCapabilities Host() =>
            new(
                [new DisplayAdapter(1, "Test Adapter", DrivesDisplay: true)],
                [new HostVideoEncoder(EncoderVendor.Nvenc, 1, new HashSet<VideoCodec> { VideoCodec.H264 })],
                1,
                26100,
                EncodersProbed: true,
                ScreenCaptureBackend: CaptureApi.DesktopDuplication);

        internal static NetworkPath Path() => new(4.0, 1.0, 120.0, 0.0);
    }

    private sealed class FakeHostProbe(HostCapabilities host) : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(host);
    }

    private sealed class FakeDeviceProbe(FireTvDevice? device) : IDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>(device is null ? [] : [device]);

        public Task<FireTvDevice> IdentifyAsync(FireTvDevice device, CancellationToken cancellationToken = default) =>
            Task.FromResult(device);
    }

    private sealed class FakeNetworkProbe(NetworkPath? path) : INetworkProbe
    {
        public Task<NetworkPath?> MeasureAsync(FireTvDevice device, CancellationToken cancellationToken = default) =>
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
}
