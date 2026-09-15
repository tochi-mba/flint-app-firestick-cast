using System.Net;
using Flint.Core;

namespace Flint.Core.Tests.TestData;

/// <summary>
/// Builders for capability-assessment inputs.
/// </summary>
/// <remarks>
/// Defaults describe a working setup, so each test states only the one thing it is about. That
/// keeps the interesting difference visible on the line that sets it.
/// </remarks>
internal static class Build
{
    internal const long DiscreteLuid = 1001L;
    internal const long IntegratedLuid = 1002L;

    internal static FireTvDevice Device(
        FireTvPlatform platform = FireTvPlatform.FireOs8,
        AdbConnectionState adbState = AdbConnectionState.Connected) =>
        new(IPAddress.Parse("192.168.1.42"), "Living Room", DiscoverySource.MulticastDns)
        {
            Platform = platform,
            AdbState = adbState,
            AdbPort = 5555,
            Model = "AFTKA",
            AndroidRelease = "11",
            AndroidApiLevel = 30,
        };

    internal static HostVideoEncoder Nvenc(long adapterLuid = DiscreteLuid) =>
        new(EncoderVendor.Nvenc, adapterLuid, new HashSet<VideoCodec>
        {
            VideoCodec.H264,
            VideoCodec.H265,
            VideoCodec.Av1,
        });

    internal static HostVideoEncoder QuickSync(long adapterLuid = IntegratedLuid) =>
        new(EncoderVendor.QuickSync, adapterLuid, new HashSet<VideoCodec>
        {
            VideoCodec.H264,
            VideoCodec.H265,
        });

    internal static HostVideoEncoder SoftwareOnly() =>
        new(EncoderVendor.Software, IntegratedLuid, new HashSet<VideoCodec> { VideoCodec.H264 });

    /// <summary>A hybrid laptop: integrated GPU drives the display, discrete GPU holds NVENC.</summary>
    /// <param name="captureBackend">
    /// Defaults to a working backend so a test about encoders or the network is not silently
    /// diverted into the capture gate. Pass <see langword="null"/> to exercise that gate.
    /// </param>
    internal static HostCapabilities Host(
        IReadOnlyList<HostVideoEncoder>? encoders = null,
        int windowsBuild = 26100,
        CaptureApi? captureBackend = CaptureApi.WindowsGraphicsCapture) =>
        new(
            [
                new DisplayAdapter(DiscreteLuid, "NVIDIA GeForce RTX 4070 Laptop GPU", DrivesDisplay: false),
                new DisplayAdapter(IntegratedLuid, "Intel(R) UHD Graphics", DrivesDisplay: true),
            ],
            encoders ?? [Nvenc(), QuickSync()],
            IntegratedLuid,
            windowsBuild,
            EncodersProbed: true,
            ScreenCaptureBackend: captureBackend);

    internal static NetworkPath Path(
        double roundTripMs = 4.0,
        double jitterMs = 1.0,
        double throughputMbps = 120.0,
        double packetLossPercent = 0.0) =>
        new(roundTripMs, jitterMs, throughputMbps, packetLossPercent);
}
