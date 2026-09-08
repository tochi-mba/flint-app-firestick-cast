using System.Runtime.InteropServices;
using Flint.Core;
using Shouldly;

namespace Flint.Engine.Interop.Tests;

public sealed class EngineHostProbeTests
{
    [Fact]
    public async Task ProbeAsync_NativeUnavailable_PreservesTheHonestFallback()
    {
        var fallback = Host(encodersProbed: false);
        var probe = new EngineHostProbe(new StubHostProbe(fallback), new StubEngineProbe(success: false));

        var result = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        result.ShouldBeSameAs(fallback);
        result.EncodersProbed.ShouldBeFalse();
    }

    [Fact]
    public async Task ProbeAsync_NativeAvailable_ReplacesAdaptersAndEncodersAsOneIdentitySpace()
    {
        var fallback = Host(encodersProbed: false);
        var nativeAdapters = new[] { new DisplayAdapter(700, "Real DXGI adapter", true) };
        var nativeEncoders = new[]
        {
            new HostVideoEncoder(EncoderVendor.Nvenc, 700, new HashSet<VideoCodec> { VideoCodec.H264 }),
        };
        var native = new EngineProbeResult(nativeAdapters, nativeEncoders, 700);
        var probe = new EngineHostProbe(new StubHostProbe(fallback), new StubEngineProbe(true, native));

        var result = await probe.ProbeAsync(TestContext.Current.CancellationToken);

        result.Adapters.ShouldBe(nativeAdapters);
        result.Encoders.ShouldBe(nativeEncoders);
        result.PrimaryDisplayAdapterLuid.ShouldBe(700);
        result.WindowsBuild.ShouldBe(fallback.WindowsBuild);
        result.EncodersProbed.ShouldBeTrue();
    }

    [Fact]
    public async Task ProbeAsync_CancellationAfterFallback_DoesNotCrossTheNativeBoundary()
    {
        using var cancellation = new CancellationTokenSource();
        var engine = new StubEngineProbe(success: true);
        var probe = new EngineHostProbe(
            new StubHostProbe(Host(false), () => cancellation.Cancel()),
            engine);

        await Should.ThrowAsync<OperationCanceledException>(
            () => probe.ProbeAsync(cancellation.Token));

        engine.CallCount.ShouldBe(0);
    }

    [Fact]
    public void Constructor_RejectsMissingDependencies()
    {
        Should.Throw<ArgumentNullException>(() => new EngineHostProbe(null!));
        Should.Throw<ArgumentNullException>(
            () => new EngineHostProbe(new StubHostProbe(Host(false)), null!));
    }

    [Fact]
    public void NativeLayouts_MatchTheRustAbiExactly()
    {
        Marshal.SizeOf<NativeEncoder>().ShouldBe(24);
        Marshal.SizeOf<NativeAdapter>().ShouldBe(272);
    }

    [Theory]
    [InlineData(0, EncoderVendor.Nvenc)]
    [InlineData(1, EncoderVendor.Amf)]
    [InlineData(2, EncoderVendor.QuickSync)]
    [InlineData(3, EncoderVendor.Unknown)]
    [InlineData(255, EncoderVendor.Unknown)]
    public void NativeEncoder_MapsVendorWithoutInventingABrand(byte nativeVendor, EncoderVendor expected)
    {
        var native = new NativeEncoder
        {
            Vendor = nativeVendor,
            AdapterLuid = 42,
            CodecMask = NativeEngineProbeApi.CodecMaskH264,
        };

        var model = native.ToModel();

        model.ShouldNotBeNull();
        model.Vendor.ShouldBe(expected);
        model.AdapterLuid.ShouldBe(42);
    }

    [Fact]
    public void NativeEncoder_MapsEveryKnownCodecAndIgnoresUnknownBits()
    {
        var native = new NativeEncoder
        {
            CodecMask = NativeEngineProbeApi.CodecMaskH264
                | NativeEngineProbeApi.CodecMaskH265
                | NativeEngineProbeApi.CodecMaskAv1
                | (1u << 31),
        };

        var model = native.ToModel();

        model.ShouldNotBeNull();
        model.Codecs.ShouldBe(new[] { VideoCodec.H264, VideoCodec.H265, VideoCodec.Av1 }, ignoreOrder: true);
    }

    [Fact]
    public void NativeEncoder_WithoutAKnownCodec_IsNotReportedAsCapable()
    {
        new NativeEncoder { CodecMask = 1u << 31 }.ToModel().ShouldBeNull();
    }

    [Fact]
    public void NativeAdapter_InvalidBoolean_IsRejectedAsAnAbiMismatch()
    {
        var native = new NativeAdapter { DrivesDisplay = 2 };

        native.ToModel().ShouldBeNull();
    }

    [Fact]
    public void NativeAdapter_PrimaryOutputWithoutAnyDisplay_IsRejectedAsAnAbiMismatch()
    {
        var native = new NativeAdapter { DrivesPrimaryDisplay = 1, DrivesDisplay = 0 };

        native.ToModel().ShouldBeNull();
    }

    [Fact]
    public void NativeAdapter_BlankDescription_StillHasAnHonestDiagnosticLabel()
    {
        var native = new NativeAdapter { AdapterLuid = 0x1234, DrivesDisplay = 1 };

        var model = native.ToModel();

        model.ShouldNotBeNull();
        model.Description.ShouldContain("0000000000001234");
        model.DrivesDisplay.ShouldBeTrue();
    }

    [Fact]
    public void Conversion_RejectsCountsOutsideTheSuppliedBuffers()
    {
        NativeEngineProbeApi.ConvertAdapters([], -1).ShouldBeNull();
        NativeEngineProbeApi.ConvertAdapters([], 1).ShouldBeNull();
        NativeEngineProbeApi.ConvertEncoders([], -1).ShouldBeNull();
        NativeEngineProbeApi.ConvertEncoders([], 1).ShouldBeNull();
    }

    private static HostCapabilities Host(bool encodersProbed) => new(
        [new DisplayAdapter(1, "placeholder", true)],
        [],
        1,
        26100,
        encodersProbed);

    private sealed class StubHostProbe(
        HostCapabilities result,
        Action? beforeReturn = null) : IHostProbe
    {
        public Task<HostCapabilities> ProbeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            beforeReturn?.Invoke();
            return Task.FromResult(result);
        }
    }

    private sealed class StubEngineProbe(
        bool success,
        EngineProbeResult? result = null) : IEngineProbeApi
    {
        public int CallCount { get; private set; }

        public bool TryProbe(out EngineProbeResult inventory)
        {
            CallCount++;
            inventory = result ?? new EngineProbeResult([], [], 0);
            return success;
        }
    }
}
