using Flint.Core.Tests.TestData;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>
/// Capture-API choice is the difference between a working mirror and a silent fallback on hybrid
/// laptops, which is the machine class Flint was designed on.
/// </summary>
public sealed class CaptureStrategySelectorTests
{
    [Fact]
    public void Select_EncoderOnTheDisplayAdapter_PrefersDesktopDuplication()
    {
        // Arrange
        List<HostVideoEncoder> encoders = [Build.QuickSync(Build.IntegratedLuid)];

        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, encoders, VideoCodec.H264);

        // Assert
        strategy.ShouldNotBeNull();
        strategy.Api.ShouldBe(CaptureApi.DesktopDuplication);
        strategy.RequiresCrossAdapterCopy.ShouldBeFalse();
    }

    [Fact]
    public void Select_EncoderOnADifferentAdapter_FallsBackToWindowsGraphicsCapture()
    {
        // Desktop Duplication cannot cross adapters; choosing it here would fail at runtime.

        // Arrange
        List<HostVideoEncoder> encoders = [Build.Nvenc(Build.DiscreteLuid)];

        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, encoders, VideoCodec.H264);

        // Assert
        strategy.ShouldNotBeNull();
        strategy.Api.ShouldBe(CaptureApi.WindowsGraphicsCapture);
        strategy.RequiresCrossAdapterCopy.ShouldBeTrue();
    }

    [Fact]
    public void Select_HybridLaptop_KeepsCaptureAndEncodeOnTheSameAdapterWhenItCan()
    {
        // Both encoders are usable, but only Quick Sync avoids the cross-adapter copy.

        // Arrange
        List<HostVideoEncoder> encoders = [Build.Nvenc(Build.DiscreteLuid), Build.QuickSync(Build.IntegratedLuid)];

        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, encoders, VideoCodec.H264);

        // Assert
        strategy.ShouldNotBeNull();
        strategy.Encoder.Vendor.ShouldBe(EncoderVendor.QuickSync);
        strategy.RequiresCrossAdapterCopy.ShouldBeFalse();
    }

    [Fact]
    public void Select_SeveralCrossAdapterEncoders_PrefersNvencForItsDedicatedEncodeSilicon()
    {
        // Arrange
        List<HostVideoEncoder> encoders =
        [
            Build.QuickSync(adapterLuid: 3003L),
            Build.Nvenc(Build.DiscreteLuid),
        ];

        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, encoders, VideoCodec.H264);

        // Assert
        strategy.ShouldNotBeNull();
        strategy.Encoder.Vendor.ShouldBe(EncoderVendor.Nvenc);
    }

    [Fact]
    public void Select_NoEncoderSupportsTheCodec_ReturnsNull()
    {
        // Quick Sync in the builder advertises H.264 and H.265 but not AV1.

        // Arrange
        List<HostVideoEncoder> encoders = [Build.QuickSync()];

        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, encoders, VideoCodec.Av1);

        // Assert
        strategy.ShouldBeNull();
    }

    [Fact]
    public void Select_SoftwareEncoderOnly_ReturnsNullRatherThanAcceptingIt()
    {
        // Arrange
        List<HostVideoEncoder> encoders = [Build.SoftwareOnly()];

        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, encoders, VideoCodec.H264);

        // Assert
        strategy.ShouldBeNull();
    }

    [Fact]
    public void Select_NoEncodersAtAll_ReturnsNull()
    {
        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, [], VideoCodec.H264);

        // Assert
        strategy.ShouldBeNull();
    }

    [Fact]
    public void Select_AlwaysExplainsItsChoice()
    {
        // The rationale is shown in Diagnostics, so an empty one is a defect.

        // Arrange
        List<HostVideoEncoder> encoders = [Build.Nvenc(Build.DiscreteLuid)];

        // Act
        var strategy = CaptureStrategySelector.Select(Build.IntegratedLuid, encoders, VideoCodec.H264);

        // Assert
        strategy.ShouldNotBeNull();
        strategy.Rationale.ShouldNotBeNullOrWhiteSpace();
        strategy.Rationale.ShouldContain("different adapters");
    }

    [Fact]
    public void Select_NullEncoderList_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(
            () => CaptureStrategySelector.Select(Build.IntegratedLuid, encoders: null!, VideoCodec.H264));
    }
}
