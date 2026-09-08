using System.Buffers.Binary;
using System.Text;
using Flint.Core;
using Shouldly;

namespace Flint.Discovery.Tests;

/// <summary>
/// The ADB handshake is how Flint tells a Fire OS device from a Vega one, so its parsing must be
/// exact and its rejection of non-ADB peers must be reliable — the port scan meets both.
/// </summary>
public sealed class AdbMessageTests
{
    [Fact]
    public void Connect_AdvertisesFlintByName()
    {
        // The banner is what the user sees on the television's authorisation prompt.

        // Act
        var payload = Encoding.UTF8.GetString(AdbMessage.Connect().Payload);

        // Assert
        payload.ShouldContain("flint");
        payload.ShouldContain("rex-technologies");
    }

    [Fact]
    public void ToBytes_WritesATwentyFourByteHeaderBeforeThePayload()
    {
        // Arrange
        var message = new AdbMessage(AdbCommand.Connect, 1, 2, [9, 8, 7]);

        // Act
        var bytes = message.ToBytes();

        // Assert
        bytes.Length.ShouldBe(AdbMessage.HeaderLength + 3);
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)).ShouldBe((uint)AdbCommand.Connect);
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)).ShouldBe(1u);
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4)).ShouldBe(2u);
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4)).ShouldBe(3u);
    }

    [Fact]
    public void ToBytes_MagicIsTheCommandComplement()
    {
        // Arrange
        var bytes = new AdbMessage(AdbCommand.Auth, 0, 0, []).ToBytes();

        // Act
        var command = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20, 4));

        // Assert
        (command ^ 0xFFFF_FFFFu).ShouldBe(magic);
    }

    [Fact]
    public void ParseHeader_ReadsBackWhatToBytesWrote()
    {
        // Arrange
        var bytes = new AdbMessage(AdbCommand.Write, 7, 11, [1, 2, 3, 4]).ToBytes();

        // Act
        var header = AdbMessage.ParseHeader(bytes.AsSpan(0, AdbMessage.HeaderLength));

        // Assert
        header.Command.ShouldBe(AdbCommand.Write);
        header.Arg0.ShouldBe(7u);
        header.Arg1.ShouldBe(11u);
        header.PayloadLength.ShouldBe(4);
    }

    [Fact]
    public void ParseHeader_MismatchedMagic_ThrowsSoANonAdbPortIsRejected()
    {
        // The scan meets unrelated services on this range; they must not be read as devices.

        // Arrange
        var bytes = new AdbMessage(AdbCommand.Connect, 0, 0, []).ToBytes();
        bytes[20] ^= 0xFF;

        // Act & Assert
        Should.Throw<AdbProtocolException>(
            () => AdbMessage.ParseHeader(bytes.AsSpan(0, AdbMessage.HeaderLength)));
    }

    [Fact]
    public void ParseHeader_OversizedPayload_ThrowsRatherThanAllocating()
    {
        // An unauthenticated peer can claim any length. Flint must not honour it.

        // Arrange
        var bytes = new AdbMessage(AdbCommand.Connect, 0, 0, []).ToBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), AdbMessage.MaxPayloadLength + 1u);

        // Act & Assert
        Should.Throw<AdbProtocolException>(
            () => AdbMessage.ParseHeader(bytes.AsSpan(0, AdbMessage.HeaderLength)));
    }

    [Fact]
    public void ParseHeader_WrongLength_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => AdbMessage.ParseHeader(new byte[10]));
    }

    [Fact]
    public void Checksum_IsAnUnsignedByteSum()
    {
        // Act & Assert
        AdbMessage.Checksum([1, 2, 3]).ShouldBe(6u);
        AdbMessage.Checksum([255, 255]).ShouldBe(510u);
        AdbMessage.Checksum([]).ShouldBe(0u);
    }
}

/// <summary>Banner parsing tolerates what real devices actually send.</summary>
public sealed class AdbBannerTests
{
    [Fact]
    public void Parse_TypicalFireTvBanner_ExtractsProperties()
    {
        // Arrange
        const string raw =
            "device::ro.product.name=mantis;ro.product.model=AFTMM;ro.product.device=mantis;features=cmd\0";

        // Act
        var banner = AdbBanner.Parse(raw);

        // Assert
        banner.Name.ShouldBe("mantis");
        banner.Model.ShouldBe("AFTMM");
        banner.Device.ShouldBe("mantis");
    }

    [Fact]
    public void Parse_StripsTheTrailingNullTerminator()
    {
        // Act
        var banner = AdbBanner.Parse("device::ro.product.model=AFTKA\0");

        // Assert
        banner.Raw.ShouldNotEndWith("\0");
        banner.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public void Parse_MissingProperties_ReturnNullRatherThanEmptyStrings()
    {
        // Act
        var banner = AdbBanner.Parse("device::features=cmd");

        // Assert
        banner.Model.ShouldBeNull();
        banner.Name.ShouldBeNull();
    }

    [Fact]
    public void Parse_EmptyValue_IsTreatedAsAbsent()
    {
        // Act
        var banner = AdbBanner.Parse("device::ro.product.model=");

        // Assert
        banner.Model.ShouldBeNull();
    }

    [Fact]
    public void Parse_MalformedBanner_YieldsWhatItCanRatherThanThrowing()
    {
        // A half-answer is still evidence the device is Android.

        // Act
        var banner = AdbBanner.Parse("garbage;;=nokey;ro.product.model=AFTKA;trailing");

        // Assert
        banner.Model.ShouldBe("AFTKA");
    }

    [Fact]
    public void Parse_NoDoubleColonPrefix_StillReadsProperties()
    {
        // Act
        AdbBanner.Parse("ro.product.model=AFTKA").Model.ShouldBe("AFTKA");
    }

    [Fact]
    public void Parse_Null_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => AdbBanner.Parse(null!));
    }
}

/// <summary>
/// The resolver turns evidence into a platform, and its refusal to guess is the behaviour that
/// makes the whole capability report trustworthy.
/// </summary>
public sealed class FireTvPlatformResolverTests
{
    [Fact]
    public void Resolve_NothingAnswered_IsUnknownNotVega()
    {
        // Silence is ambiguous. Calling it Vega would be a guess dressed as a fact.

        // Act
        var platform = FireTvPlatformResolver.Resolve(AdbProbeResult.NotFound, apiLevel: 30);

        // Assert
        platform.ShouldBe(FireTvPlatform.Unknown);
    }

    [Fact]
    public void Resolve_AndroidProvenButApiLevelUnread_IsUnknown()
    {
        // Arrange
        var result = new AdbProbeResult(AdbConnectionState.Unauthorized, 5555, Banner: null);

        // Act
        FireTvPlatformResolver.Resolve(result, apiLevel: null).ShouldBe(FireTvPlatform.Unknown);
    }

    [Theory]
    [InlineData(22, FireTvPlatform.FireOs5)]
    [InlineData(25, FireTvPlatform.FireOs6)]
    [InlineData(28, FireTvPlatform.FireOs7)]
    [InlineData(29, FireTvPlatform.FireOs8)]
    [InlineData(30, FireTvPlatform.FireOs8)]
    [InlineData(31, FireTvPlatform.FireOs14)]
    [InlineData(34, FireTvPlatform.FireOs14)]
    [InlineData(35, FireTvPlatform.FireOs16)]
    [InlineData(36, FireTvPlatform.FireOs16)]
    public void Resolve_ConnectedDevice_MapsApiLevelToFireOsGeneration(int apiLevel, FireTvPlatform expected)
    {
        // Arrange
        var result = new AdbProbeResult(AdbConnectionState.Connected, 5555, AdbBanner.Parse("device::"));

        // Act
        FireTvPlatformResolver.Resolve(result, apiLevel).ShouldBe(expected);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(27)]
    [InlineData(37)]
    public void Resolve_UndocumentedFireOsApiLevel_StaysUnknown(int apiLevel)
    {
        var result = new AdbProbeResult(AdbConnectionState.Connected, 5555, AdbBanner.Parse("device::"));

        FireTvPlatformResolver.Resolve(result, apiLevel).ShouldBe(FireTvPlatform.Unknown);
    }

    [Fact]
    public void Resolve_Null_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => FireTvPlatformResolver.Resolve(null!, 30));
    }

    [Theory]
    [InlineData(AdbConnectionState.Connected, true)]
    [InlineData(AdbConnectionState.Unauthorized, true)]
    [InlineData(AdbConnectionState.Refused, false)]
    [InlineData(AdbConnectionState.TimedOut, false)]
    public void ProvesAndroid_OnlyWhenTheDeviceSpokeAdb(AdbConnectionState state, bool expected)
    {
        // Arrange
        var result = new AdbProbeResult(state, 5555, Banner: null);

        // Act & Assert
        result.ProvesAndroid.ShouldBe(expected);
    }

    [Fact]
    public void PortRange_CoversTheDocumentedFireTvSpread()
    {
        // Fire TV does not always use 5555; assuming it produces false negatives.

        // Act & Assert
        AdbProbeClient.FirstPort.ShouldBe(5555);
        AdbProbeClient.LastPort.ShouldBe(5585);
    }
}
