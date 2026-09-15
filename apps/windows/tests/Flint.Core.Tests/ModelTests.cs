using Flint.Core.Tests.TestData;
using Shouldly;

namespace Flint.Core.Tests;

/// <summary>
/// The small predicates the assessor and UI lean on. Cheap to get subtly wrong, so pinned here.
/// </summary>
public sealed class ModelTests
{
    [Theory]
    [InlineData(FireTvPlatform.FireOs5, true)]
    [InlineData(FireTvPlatform.FireOs6, true)]
    [InlineData(FireTvPlatform.FireOs7, true)]
    [InlineData(FireTvPlatform.FireOs8, true)]
    [InlineData(FireTvPlatform.FireOs14, true)]
    [InlineData(FireTvPlatform.FireOs16, true)]
    [InlineData(FireTvPlatform.Vega, false)]
    [InlineData(FireTvPlatform.Unknown, false)]
    public void CanInstallReceiver_MatchesPlatformReality(FireTvPlatform platform, bool expected)
    {
        // Act & Assert
        platform.CanInstallReceiver().ShouldBe(expected);
    }

    [Fact]
    public void CanInstallReceiver_Unknown_IsFalseBecauseAGuessIsNotACapability()
    {
        // Act & Assert
        FireTvPlatform.Unknown.CanInstallReceiver().ShouldBeFalse();
    }

    [Theory]
    [InlineData(FireTvPlatform.FireOs5, "Fire OS 5")]
    [InlineData(FireTvPlatform.FireOs6, "Fire OS 6")]
    [InlineData(FireTvPlatform.FireOs7, "Fire OS 7")]
    [InlineData(FireTvPlatform.FireOs8, "Fire OS 8")]
    [InlineData(FireTvPlatform.FireOs14, "Fire OS 14")]
    [InlineData(FireTvPlatform.FireOs16, "Fire OS 16")]
    [InlineData(FireTvPlatform.Vega, "Vega OS")]
    [InlineData(FireTvPlatform.Unknown, "Unknown")]
    public void ToDisplayLabel_ReadsAsProductNamesNotEnumNames(FireTvPlatform platform, string expected)
    {
        // Act & Assert
        platform.ToDisplayLabel().ShouldBe(expected);
    }

    [Theory]
    [InlineData(AdbConnectionState.Connected, true)]
    [InlineData(AdbConnectionState.Unauthorized, true)]
    [InlineData(AdbConnectionState.Refused, false)]
    [InlineData(AdbConnectionState.TimedOut, false)]
    [InlineData(AdbConnectionState.NotProbed, false)]
    public void IsReachable_TreatsAnUnauthorizedDeviceAsPresent(AdbConnectionState state, bool expected)
    {
        // An RSA prompt means the device answered, which is evidence it is Android-based.

        // Arrange
        var device = Build.Device(adbState: state);

        // Act & Assert
        device.IsReachable.ShouldBe(expected);
    }

    [Fact]
    public void IsSessionCapable_SoftwareEncoder_IsFalse()
    {
        // Act & Assert
        Build.SoftwareOnly().IsSessionCapable.ShouldBeFalse();
    }

    [Fact]
    public void IsSessionCapable_HardwareEncoderWithNoCodecs_IsFalse()
    {
        // Arrange
        var encoder = new HostVideoEncoder(EncoderVendor.Nvenc, Build.DiscreteLuid, new HashSet<VideoCodec>());

        // Act & Assert
        encoder.IsSessionCapable.ShouldBeFalse();
    }

    [Fact]
    public void Supports_ReportsOnlyProbedCodecs()
    {
        // Act & Assert
        Build.QuickSync().Supports(VideoCodec.H265).ShouldBeTrue();
        Build.QuickSync().Supports(VideoCodec.Av1).ShouldBeFalse();
    }

    [Fact]
    public void VideoCodec_UsesTheSameIdentifiersAsTheRexWireProtocol()
    {
        // Host and receiver must never disagree about what a codec number means.

        // Act & Assert
        ((int)VideoCodec.H264).ShouldBe(1);
        ((int)VideoCodec.H265).ShouldBe(2);
    }

    [Theory]
    [InlineData(120.0, 4.0, true)]
    [InlineData(20.0, 30.0, true)]
    [InlineData(19.9, 4.0, false)]
    [InlineData(120.0, 30.1, false)]
    public void SupportsMirroring_IsInclusiveAtBothThresholds(
        double throughputMbps,
        double roundTripMs,
        bool expected)
    {
        // Arrange
        var path = Build.Path(roundTripMs: roundTripMs, throughputMbps: throughputMbps);

        // Act & Assert
        path.SupportsMirroring.ShouldBe(expected);
    }

    [Fact]
    public void IsHybridGraphics_TwoAdapters_IsTrue()
    {
        // Act & Assert
        Build.Host().IsHybridGraphics.ShouldBeTrue();
    }

    [Fact]
    public void HasSessionCapableEncoder_SoftwareOnly_IsFalse()
    {
        // Act & Assert
        Build.Host([Build.SoftwareOnly()]).HasSessionCapableEncoder.ShouldBeFalse();
    }

    [Fact]
    public void IsOfferable_OnlyAvailableVerdictsMayBecomeControls()
    {
        // Act & Assert
        foreach (var status in Enum.GetValues<ModeStatus>())
        {
            var verdict = new ModeVerdict(CastMode.Mirror, status, "reason");
            verdict.IsOfferable.ShouldBe(status == ModeStatus.Available);
        }
    }

    [Fact]
    public void Indexer_UnknownMode_ThrowsRatherThanReturningAFalseVerdict()
    {
        // Arrange
        var report = new CapabilityReport(Build.Device(), Build.Host(), Build.Path(), []);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => report[CastMode.Mirror]);
    }
}
