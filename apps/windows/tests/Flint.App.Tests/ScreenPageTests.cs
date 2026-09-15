using System.Net;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Flint.App.ViewModels;
using Flint.App.Views;
using Flint.Core;
using Shouldly;

namespace Flint.App.Tests;

/// <summary>
/// The Screen page rendered through its real template.
/// </summary>
/// <remarks>
/// This page shipped a bug that a view-model-only test could not have caught: the button's
/// <c>IsEnabled</c> binding in the XAML disagreed with what the capability report actually said,
/// so a person could press "start" on a mode that was never going to work. A view-model test
/// proves the data is correct; only rendering the real template proves the binding that reads it
/// is correct too.
/// </remarks>
public sealed class ScreenPageTests
{
    [AvaloniaFact]
    public async Task StartButton_IsDisabled_WhenMirrorIsNotOfferable()
    {
        // The regression this pins had the opposite XAML binding — enabled whenever a session
        // happened to be connected, regardless of what the capability report said.

        // Arrange: a PC with no hardware encoder, which blocks mirroring.
        var page = Render(Fake.Device(), Fake.HostThatCannotMirror(), Fake.Path());

        // Act
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert
        var button = StartButton(page.View);
        button.IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task ReasonText_MatchesTheMirrorVerdictExactly()
    {
        // The page must show the capability report's own words, not a hand-written caption that
        // can drift out of sync with what actually happened — which is exactly how the old copy
        // ("not available in this build") kept describing a permanently broken state as routine.

        // Arrange
        var page = Render(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert
        var expectedReason = page.ViewModel.MirrorVerdict!.Reason;
        Texts(page.View).ShouldContain(expectedReason);
    }

    [AvaloniaFact]
    public void BeforeAnyProbe_TheStartButtonIsAlsoDisabled()
    {
        // No verdict exists yet, so there is nothing to offer.

        // Arrange
        var page = Render(Fake.Device(), Fake.Host(), Fake.Path());

        // Act
        page.View.UpdateLayout();

        // Assert
        StartButton(page.View).IsEnabled.ShouldBeFalse();
    }

    [AvaloniaFact]
    public async Task PressingTheDisabledButtonProgrammatically_NeverClaimsSuccess()
    {
        // Defence in depth: even if something invokes the command directly rather than through
        // the disabled button, it must refuse rather than tell the receiver to expect a stream
        // that nothing will send.

        // Arrange
        var page = Render(Fake.Device(), Fake.Host(), Fake.Path());
        await page.ViewModel.ProbeCommand.ExecuteAsync(null);

        // Act
        await page.ViewModel.StartScreenSessionCommand.ExecuteAsync(null);
        page.View.UpdateLayout();

        // Assert
        page.ViewModel.Failure.ShouldNotBeNull();
        page.ViewModel.IsConnected.ShouldBeFalse();
    }

    private static Button StartButton(Control root) =>
        root.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "START SCREEN SESSION"));

    private static List<string> Texts(Control root) =>
    [
        .. root.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!),
    ];

    private static (ScreenPage View, CastPageViewModel ViewModel) Render(
        FireTvDevice? device,
        HostCapabilities host,
        NetworkPath? path)
    {
        // The address store must be isolated. CastPageViewModel's default constructor reads real,
        // persisted recent-address history from this machine's actual %LOCALAPPDATA%; without an
        // explicit empty store here, a leftover manual address from real use of the app makes
        // ProbeCommand silently take the direct-address path instead of ordinary discovery, which
        // the fakes below do not implement.
        var viewModel = new CastPageViewModel(
            new CapabilityProber(
                new FakeHostProbe(host),
                new FakeDeviceProbe(device),
                new FakeNetworkProbe(path)),
            new EmptyRecentAddressStore());
        var view = new ScreenPage { DataContext = viewModel };
        var window = new Window { Width = 1180, Height = 780, Content = view };
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

    private sealed class FakeDeviceProbe(FireTvDevice? device) : IDeviceProbe
    {
        public Task<IReadOnlyList<FireTvDevice>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FireTvDevice>>(device is null ? [] : [device]);
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
