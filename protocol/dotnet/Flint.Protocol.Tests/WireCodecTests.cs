using System.Buffers.Binary;
using Shouldly;

namespace Flint.Protocol.Tests;

/// <summary>
/// Cross-language interoperability. These are the tests that actually stop three implementations
/// drifting apart; a round-trip test proves only that C# agrees with itself.
/// </summary>
public sealed class GoldenVectorTests
{
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryCase_EncodesToTheBytesRustCommitted(string name)
    {
        // Arrange
        var expected = File.ReadAllBytes(Path.Combine(GoldenVectors.Directory(), name + ".bin"));

        // Act
        var actual = WireCodec.Encode(GoldenVectors.All[name]);

        // Assert
        actual.ShouldBe(
            expected,
            $"{name} does not match the committed bytes. If the wire format changed deliberately, "
            + "regenerate the vectors in Rust and review the diff as a protocol change.");
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryCase_DecodesFromTheBytesRustCommitted(string name)
    {
        // Arrange
        var bytes = File.ReadAllBytes(Path.Combine(GoldenVectors.Directory(), name + ".bin"));
        var expected = GoldenVectors.All[name];

        // Act
        var decoded = WireCodec.Decode(bytes);

        // Assert
        decoded.Flags.ShouldBe(expected.Flags);
        decoded.ProtocolVersion.ShouldBe(expected.ProtocolVersion);
        decoded.Message.ShouldBe(Normalise(expected.Message));
    }

    [Fact]
    public void EveryCommittedVector_HasACaseInThisSuite()
    {
        // A vector Rust commits but C# never asserts is an untested half of the contract.

        // Arrange
        var committed = System.IO.Directory
            .EnumerateFiles(GoldenVectors.Directory(), "*.bin")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        // Act & Assert
        committed.ShouldNotBeEmpty("the golden corpus should not be empty");
        foreach (var name in committed)
        {
            GoldenVectors.All.ShouldContainKey(
                name!,
                $"{name} is committed but has no C# case. Add it to GoldenVectors, or delete the file.");
        }
    }

    [Fact]
    public void EveryCaseInThisSuite_HasACommittedVector()
    {
        // Act & Assert
        foreach (var name in GoldenVectors.All.Keys)
        {
            File.Exists(Path.Combine(GoldenVectors.Directory(), name + ".bin")).ShouldBeTrue(
                $"{name} has a C# case but no committed vector. Regenerate in Rust.");
        }
    }

    /// <summary>
    /// Applies the normalisation the encoder performs, so a decoded case can be compared to source.
    /// </summary>
    /// <remarks>
    /// Only Hello normalises: its codec list is sorted and deduplicated on the wire, so the
    /// hello-full case cannot round-trip to the unsorted list it was written with.
    /// </remarks>
    private static WireMessage Normalise(WireMessage message) => message switch
    {
        HelloMessage hello => hello with { CodecCapabilities = hello.NormalisedCodecs },
        _ => message,
    };

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in GoldenVectors.All.Keys)
        {
            data.Add(name);
        }

        return data;
    }
}

/// <summary>
/// Framing, and the strictness that protects an unauthenticated receive path.
/// </summary>
public sealed class WireFramingTests
{
    [Fact]
    public void Encode_WritesTheRexEnvelope()
    {
        // Act
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));

        // Assert
        BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, 4)).ShouldBe(bytes.Length - 4);
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)).ShouldBe((ushort)0x5243);
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6, 2)).ShouldBe((ushort)ProtocolVersion.Current);
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8, 2)).ShouldBe((ushort)WireMessageType.Bye);
    }

    [Fact]
    public void Decode_BadMagic_IsRejectedSoANonProtocolPeerIsNotMisread()
    {
        // The port range Flint scans meets unrelated services; they must not decode as frames.

        // Arrange
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));
        bytes[4] ^= 0xFF;

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes))
            .Message.ShouldContain("magic");
    }

    [Fact]
    public void Decode_TrailingBytes_AreRejected()
    {
        // Arrange
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));
        var padded = bytes.Concat<byte>([0]).ToArray();

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(padded))
            .Message.ShouldContain("Trailing");
    }

    [Fact]
    public void DecodePrefix_ReadsConsecutiveFramesFromOneBuffer()
    {
        // How a stream reader consumes a TCP segment carrying more than one frame.

        // Arrange
        var first = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));
        var second = WireCodec.Encode(new WireFrame(new StatsMessage(1, 2, 3, 4)));
        var buffer = first.Concat(second).ToArray();

        // Act
        var frameOne = WireCodec.DecodePrefix(buffer, out var consumedOne);
        var frameTwo = WireCodec.DecodePrefix(buffer.AsSpan(consumedOne), out var consumedTwo);

        // Assert
        frameOne.Message.ShouldBeOfType<ByeMessage>();
        frameTwo.Message.ShouldBeOfType<StatsMessage>();
        (consumedOne + consumedTwo).ShouldBe(buffer.Length);
    }

    [Fact]
    public void DecodePrefix_EveryTruncation_IsReportedAsTruncationNotCorruption()
    {
        // A stream reader relies on this distinction to choose between "read more" and "give up".

        // Arrange
        var bytes = WireCodec.Encode(new WireFrame(new StatsMessage(1, 2, 3, 4)));

        // Act & Assert
        for (var cut = 1; cut < bytes.Length; cut++)
        {
            var slice = bytes.AsSpan(0, cut).ToArray();
            Should.Throw<WireTruncatedException>(
                () => WireCodec.DecodePrefix(slice, out _),
                $"cutting at {cut} should read as truncation");
        }
    }

    [Fact]
    public void Decode_ZeroProtocolVersion_IsRejected()
    {
        // Arrange
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6, 2), 0);

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes));
    }

    [Fact]
    public void Decode_ZeroMessageType_IsRejected()
    {
        // Arrange
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8, 2), 0);

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes));
    }

    [Fact]
    public void Decode_AbsurdDeclaredLength_IsRejectedRatherThanAllocated()
    {
        // An unauthenticated peer can claim any length. Flint must not honour it.

        // Arrange
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), int.MaxValue);

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes))
            .Message.ShouldContain("body length");
    }

    [Fact]
    public void Decode_BodyLengthBelowTheEnvelope_IsRejected()
    {
        // Arrange
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal)));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), WireCodec.EnvelopeLength - 1);

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes));
    }

    [Fact]
    public void UnknownMessageAndFlags_SurviveAFutureVersionRoundTrip()
    {
        // Forward compatibility: an older build must relay what it cannot read.

        // Arrange
        var original = new WireFrame(
            ProtocolVersion: 99,
            Message: new UnknownMessage(0xfefe, BinaryData.From([1, 2, 3])),
            Flags: 0xabcd);

        // Act
        var decoded = WireCodec.Decode(WireCodec.Encode(original));

        // Assert
        decoded.ShouldBe(original);
    }

    [Fact]
    public void KnownMessageAtAnUnsupportedVersion_IsRejected()
    {
        // Unlike an unknown type, a known one at a version we do not implement cannot be guessed at.

        // Act & Assert
        Should.Throw<UnsupportedProtocolVersionException>(
            () => WireCodec.Encode(new WireFrame(ProtocolVersion.Current + 1, new ByeMessage(ByeReason.Normal))));
    }

    [Fact]
    public void Decode_MalformedUtf8_IsRejectedRatherThanSubstituted()
    {
        // A replacement character would let a peer smuggle a different string past a later
        // comparison, so strict decoding is a security property rather than a nicety.

        // Arrange: a Bye frame whose detail bytes are an invalid UTF-8 sequence.
        var bytes = WireCodec.Encode(new WireFrame(new ByeMessage(ByeReason.Normal, "ab")));
        bytes[^2] = 0xC3;
        bytes[^1] = 0x28;

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes))
            .Message.ShouldContain("UTF-8");
    }

    [Fact]
    public void Decode_BooleanThatIsNeitherZeroNorOne_IsRejected()
    {
        // Arrange: the key-frame flag sits immediately after the 8-byte presentation time.
        var bytes = WireCodec.Encode(new WireFrame(
            new VideoPacket(0, KeyFrame: true, BinaryData.From([1]))));
        bytes[4 + WireCodec.EnvelopeLength + 8] = 2;

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Decode(bytes));
    }

    [Fact]
    public void Encode_NullFrame_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => WireCodec.Encode(null!));
    }
}

/// <summary>Invariants the encoder refuses to put on the wire.</summary>
public sealed class WireInvariantTests
{
    [Fact]
    public void Encode_EmptyVideoPacket_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(
            new WireFrame(new VideoPacket(0, KeyFrame: true, BinaryData.Empty))));
    }

    [Fact]
    public void Encode_NegativePresentationTime_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(
            new WireFrame(new VideoPacket(-1, KeyFrame: true, BinaryData.From([1])))));
    }

    [Fact]
    public void Encode_EmptyCredential_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(
            new WireFrame(new AuthMessage(AuthMethod.PairingCode, BinaryData.Empty))));
    }

    [Fact]
    public void Encode_SeekWithoutAPosition_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new ControlMessage(1, new TransportControl(TransportAction.SeekTo)))));
    }

    [Fact]
    public void Encode_NonSeekWithAPosition_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new ControlMessage(1, new TransportControl(TransportAction.Play, 500)))));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Encode_VolumeOutsideItsRange_IsRejected(float level)
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new ControlMessage(1, new VolumeControl(level)))));
    }

    [Fact]
    public void Encode_NonFinitePointer_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new ControlMessage(1, new PointerControl(PointerAction.Move, float.NaN, 0f)))));
    }

    [Fact]
    public void Encode_EmptyDeviceName_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new HelloMessage(1, 1, string.Empty, ValueList<CodecId>.Empty, 1, 1, 1))));
    }

    [Fact]
    public void Encode_InvertedHelloVersionRange_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new HelloMessage(3, 1, "TV", ValueList<CodecId>.Empty, 1, 1, 1))));
    }

    [Fact]
    public void Encode_NegativeStatsCounter_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(
            new WireFrame(new StatsMessage(0, -1, 0, 0))));
    }

    [Fact]
    public void Encode_OverlongDeviceName_IsRejected()
    {
        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new HelloMessage(1, 1, new string('a', 256), ValueList<CodecId>.Empty, 1, 1, 1))));
    }

    [Fact]
    public void Encode_TooManyVideoConfigBlocks_IsRejected()
    {
        // Arrange
        var blocks = ValueList<BinaryData>.From(Enumerable.Repeat(BinaryData.From([1]), 17));

        // Act & Assert
        Should.Throw<WireFormatException>(() => WireCodec.Encode(new WireFrame(
            new VideoConfigMessage(CodecId.H264, 16, 16, blocks))));
    }

    [Fact]
    public void PlaybackState_RoundTripsWithReceiverProgressAndDetail()
    {
        var frame = new WireFrame(new PlaybackStateMessage(PlaybackState.Playing, 1_250, 5_000, "Playing on this TV"));

        var decoded = WireCodec.Decode(WireCodec.Encode(frame));

        decoded.ShouldBe(frame);
    }

    [Fact]
    public void MediaData_RoundTripsChunkBytesAndFinalFlag()
    {
        var frame = new WireFrame(new MediaDataMessage(BinaryData.From([1, 2, 3, 4, 5]), IsFinal: true));

        var decoded = WireCodec.Decode(WireCodec.Encode(frame));

        decoded.ShouldBe(frame);
        decoded.Message.ShouldBeOfType<MediaDataMessage>().IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void MediaData_ANonFinalChunkRoundTripsToo()
    {
        var frame = new WireFrame(new MediaDataMessage(BinaryData.From([9, 8, 7]), IsFinal: false));

        var decoded = WireCodec.Decode(WireCodec.Encode(frame));

        decoded.Message.ShouldBeOfType<MediaDataMessage>().IsFinal.ShouldBeFalse();
    }

    [Fact]
    public void MediaCommand_LoadWithNoUrl_IsAcceptedAndRoundTrips()
    {
        // An empty Url is not a mistake here: it means "play what was just pushed over this
        // connection via MediaDataMessage", the fallback for a receiver that cannot open its own
        // outbound connection back to the host. Only the MIME type stays mandatory for a load.
        var frame = new WireFrame(new MediaCommandMessage(MediaAction.Load, Url: "", "Clip", "video/mp4"));

        var decoded = WireCodec.Decode(WireCodec.Encode(frame));

        decoded.ShouldBe(frame);
        decoded.Message.ShouldBeOfType<MediaCommandMessage>().Url.ShouldBe(string.Empty);
    }

    [Fact]
    public void MediaCommand_LoadWithNoMimeType_IsStillRejected()
    {
        Should.Throw<WireFormatException>(() => WireCodec.Encode(
            new WireFrame(new MediaCommandMessage(MediaAction.Load, Url: "", Title: "Clip", MimeType: ""))));
    }
}

/// <summary>Version negotiation, which decides whether a session can exist at all.</summary>
public sealed class ProtocolVersionTests
{
    [Fact]
    public void Negotiate_ChoosesTheHighestOverlap()
    {
        // Act & Assert
        ProtocolVersion.Negotiate(1, 3, 2, 4).ShouldBe(3);
        ProtocolVersion.Negotiate(1, 1, 1, 1).ShouldBe(1);
    }

    [Fact]
    public void Negotiate_NoOverlap_RefusesRatherThanDowngrading()
    {
        // A downgrade to a version neither side implements is worse than a clear refusal.

        // Act & Assert
        ProtocolVersion.Negotiate(5, 6, 1, 4).ShouldBeNull();
        ProtocolVersion.Negotiate(1, 2, 3, 4).ShouldBeNull();
    }

    [Fact]
    public void Negotiate_InvertedRange_IsRejected()
    {
        // Act & Assert
        ProtocolVersion.Negotiate(3, 2, 1, 4).ShouldBeNull();
        ProtocolVersion.Negotiate(1, 4, 3, 2).ShouldBeNull();
    }

    [Fact]
    public void CodecId_ZeroIsInvalidSoAZeroedBufferCannotDecode()
    {
        // Act & Assert
        new CodecId(0).IsValid.ShouldBeFalse();
        CodecId.H264.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void CodecIds_MatchTheKotlinImplementation()
    {
        // Act & Assert
        CodecId.H264.Value.ShouldBe(1);
        CodecId.H265.Value.ShouldBe(2);
        CodecId.AacLc.Value.ShouldBe(3);
        CodecId.Opus.Value.ShouldBe(4);
    }
}
