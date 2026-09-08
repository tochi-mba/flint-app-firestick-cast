using System.Runtime.InteropServices;
using Flint.Core;
using Shouldly;

namespace Flint.Engine.Interop.Tests;

/// <summary>
/// The mirror boundary is where a layout mistake corrupts memory rather than throwing, so these
/// pin the byte-for-byte agreement with the engine's own assertions in <c>ffi.rs</c> and the
/// enum values both sides match on.
/// </summary>
public sealed class NativeMirrorEngineTests
{
    [Fact]
    public void NativeMirrorConfig_MatchesTheEngineLayout()
    {
        // The engine asserts sixteen bytes on its side. A mismatch here would read the caller's
        // stack as configuration.
        Marshal.SizeOf<NativeMirrorConfig>().ShouldBe(16);
    }

    [Fact]
    public void NativeMirrorFrame_MatchesTheEngineLayout()
    {
        Marshal.SizeOf<NativeMirrorFrame>().ShouldBe(24);
    }

    [Fact]
    public void NativeMirrorStats_MatchesTheEngineLayout()
    {
        Marshal.SizeOf<NativeMirrorStats>().ShouldBe(40);
    }

    [Fact]
    public void NativeMirrorFrame_PlacesThePresentationTimeWhereTheEngineWritesIt()
    {
        // The explicit padding after the key-frame flag exists precisely so this offset is stated
        // on both sides rather than left to each compiler.
        Marshal.OffsetOf<NativeMirrorFrame>(nameof(NativeMirrorFrame.PresentationTimeUs))
            .ShouldBe(8);
    }

    [Fact]
    public void NativeMirrorStructs_PlaceEveryCrossBoundaryFieldAtTheEngineOffset()
    {
        Marshal.OffsetOf<NativeMirrorConfig>(nameof(NativeMirrorConfig.OutputIndex)).ShouldBe(0);
        Marshal.OffsetOf<NativeMirrorConfig>(nameof(NativeMirrorConfig.FrameRate)).ShouldBe(4);
        Marshal.OffsetOf<NativeMirrorConfig>(nameof(NativeMirrorConfig.BitrateBitsPerSecond)).ShouldBe(8);
        Marshal.OffsetOf<NativeMirrorConfig>(nameof(NativeMirrorConfig.MaxWidth)).ShouldBe(12);

        Marshal.OffsetOf<NativeMirrorFrame>(nameof(NativeMirrorFrame.Kind)).ShouldBe(0);
        Marshal.OffsetOf<NativeMirrorFrame>(nameof(NativeMirrorFrame.KeyFrame)).ShouldBe(4);
        Marshal.OffsetOf<NativeMirrorFrame>(nameof(NativeMirrorFrame.PresentationTimeUs)).ShouldBe(8);
        Marshal.OffsetOf<NativeMirrorFrame>(nameof(NativeMirrorFrame.ByteCount)).ShouldBe(16);

        Marshal.OffsetOf<NativeMirrorStats>(nameof(NativeMirrorStats.FramesEncoded)).ShouldBe(0);
        Marshal.OffsetOf<NativeMirrorStats>(nameof(NativeMirrorStats.FramesUnchanged)).ShouldBe(8);
        Marshal.OffsetOf<NativeMirrorStats>(nameof(NativeMirrorStats.Recoveries)).ShouldBe(16);
        Marshal.OffsetOf<NativeMirrorStats>(nameof(NativeMirrorStats.BytesEncoded)).ShouldBe(24);
        Marshal.OffsetOf<NativeMirrorStats>(nameof(NativeMirrorStats.Width)).ShouldBe(32);
        Marshal.OffsetOf<NativeMirrorStats>(nameof(NativeMirrorStats.Height)).ShouldBe(36);
    }

    [Theory]
    [InlineData(MirrorTickKind.Nothing, 0)]
    [InlineData(MirrorTickKind.Encoded, 1)]
    [InlineData(MirrorTickKind.Recovered, 2)]
    public void MirrorTickKind_MatchesTheEnginesTickValues(MirrorTickKind kind, int expected)
    {
        // These come straight off the wire from the engine. Renumbering one silently changes what
        // a tick means, and the failure would look like a stalled mirror rather than a mismatch.
        ((int)kind).ShouldBe(expected);
    }

    [Fact]
    public void DescribeStartFailure_APlatformRefusal_ExplainsWhatOnThisMachineWouldCauseIt()
    {
        // "Status -3" tells a person nothing they can act on. The two real causes — another
        // program holding the capture, or no encoder installed — are both worth naming.
        var message = NativeMirrorEngine.DescribeStartFailure(FlintStatus.PlatformError);

        message.ShouldContain("encoder");
        message.ShouldNotContain("-3");
    }

    [Fact]
    public void DescribeStartFailure_AnInternalError_IsNamedAsAFlintDefectRatherThanAUsersProblem()
    {
        // Sending someone hunting through driver settings for a bug in Flint wastes their evening.
        var message = NativeMirrorEngine.DescribeStartFailure(FlintStatus.InternalError);

        message.ShouldContain("defect in Flint");
    }

    [Fact]
    public void DescribeStartFailure_AnUnexpectedStatus_StillCarriesTheCodeSoItCanBeDiagnosed()
    {
        var message = NativeMirrorEngine.DescribeStartFailure(FlintStatus.NullArgument);

        message.ShouldContain("-1");
    }

    [Fact]
    public void DescribeStartFailure_InvalidMirrorSettingsNameTheRejectedRanges()
    {
        var message = NativeMirrorEngine.DescribeStartFailure(FlintStatus.InvalidArgument);

        message.ShouldContain("Frame rate");
        message.ShouldContain("maximum width");
        message.ShouldNotContain("-5");
    }

    [Fact]
    public void FlintStatus_InvalidArgumentMatchesTheNativeAbiValue()
    {
        ((int)FlintStatus.InvalidArgument).ShouldBe(-5);
    }

    [Fact]
    public void Start_WithoutOptions_FailsImmediatelyRatherThanCallingIntoNativeCode()
    {
        Should.Throw<ArgumentNullException>(() => new NativeMirrorEngine().Start(null!));
    }

    [Fact]
    public void Start_WhenTheEngineIsPresent_ResolvesTheMirrorAbiRatherThanSkippingQuietly()
    {
        // Every live test below tolerates a host that cannot mirror, because a build agent has no
        // screen. That tolerance also swallows a stale engine binary: the entry points go missing,
        // each live test returns early, and the suite reports green having exercised nothing.
        //
        // So the tolerance is bounded here. If the engine loads at all, it must expose the mirror
        // ABI this wrapper was compiled against — anything else is a stale build, not a machine
        // without a screen.
        if (!EngineIsLoadable())
        {
            return;
        }

        try
        {
            using var session = new NativeMirrorEngine().Start(new MirrorSessionOptions());
        }
        catch (MirrorEngineException exception)
        {
            exception.InnerException.ShouldNotBeOfType<EntryPointNotFoundException>(
                "the engine binary is older than this wrapper — rebuild flint-engine");
        }
    }

    [Fact]
    public void MirrorAbi_WhenTheEngineIsPresent_ExportsTheWholeSessionContract()
    {
        // A display-less build machine cannot successfully start a mirror, so exercising Start
        // there proves only that one symbol exists. Resolve the complete table directly: this is
        // the regression guard for shipping a DLL built before the rest of the ABI landed.
        var enginePath = Path.Combine(AppContext.BaseDirectory, "flint_engine.dll");
        if (!File.Exists(enginePath))
        {
            return;
        }

        NativeLibrary.TryLoad(enginePath, out var library)
            .ShouldBeTrue("the copied native engine must be loadable when it is present");

        try
        {
            foreach (var export in NativeMirrorEngine.RequiredAbiExports)
            {
                NativeLibrary.TryGetExport(library, export, out _)
                    .ShouldBeTrue($"the native engine is missing {export}");
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    [Fact]
    public void ToMirrorTick_RejectsAnUnknownKindInsteadOfSilentlyTreatingItAsIdle()
    {
        var frame = ValidFrame();
        frame.Kind = 99;

        Action action = () => _ = NativeMirrorEngine.ToMirrorTick(FlintStatus.Ok, frame, 1024);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("unknown mirror tick kind");
    }

    [Fact]
    public void ToMirrorTick_RejectsAByteCountOutsideThePinnedBuffer()
    {
        var frame = ValidFrame();
        frame.ByteCount = 1025;

        Action action = () => _ = NativeMirrorEngine.ToMirrorTick(FlintStatus.Ok, frame, 1024);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("invalid access-unit length");
    }

    [Fact]
    public void ToMirrorTick_BufferTooSmallPreservesTheRequiredLengthInTheError()
    {
        var frame = ValidFrame();
        frame.ByteCount = 5_000_000;

        Action action = () =>
            _ = NativeMirrorEngine.ToMirrorTick(FlintStatus.BufferTooSmall, frame, 4_194_304);

        var error = Should.Throw<MirrorEngineException>(action);
        error.Message.ShouldContain("5000000");
        error.Message.ShouldContain("4194304");
    }

    [Fact]
    public void ToMirrorTick_RejectsADirtyNativeBoolean()
    {
        var frame = ValidFrame();
        frame.KeyFrame = 2;

        Action action = () => _ = NativeMirrorEngine.ToMirrorTick(FlintStatus.Ok, frame, 1024);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("key-frame flag");
    }

    [Fact]
    public void ToVideoCodec_RejectsAnIdentifierThisManagedBuildCannotDescribe()
    {
        Action action = () => _ = NativeMirrorEngine.ToVideoCodec(FlintStatus.Ok, 99);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("unknown video codec");
    }

    [Fact]
    public void ValidateCodecSpecificData_AcceptsSeparateSpsAndPpsWithEitherAnnexBStartCode()
    {
        byte[][] blocks =
        [
            [0, 0, 0, 1, 0x67, 0x64, 0x00, 0x1f],
            [0, 0, 1, 0x68, 0xee, 0x3c, 0x80],
        ];

        Should.NotThrow(() => NativeMirrorEngine.ValidateCodecSpecificData(VideoCodec.H264, blocks));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void ValidateCodecSpecificData_RejectsAnythingOtherThanOneSpsAndOnePps(int blockCount)
    {
        var blocks = Enumerable.Range(0, blockCount)
            .Select(index => index == 0
                ? new byte[] { 0, 0, 0, 1, 0x67, 0x64 }
                : new byte[] { 0, 0, 0, 1, 0x68, 0xee })
            .ToArray();

        Action action = () => NativeMirrorEngine.ValidateCodecSpecificData(VideoCodec.H264, blocks);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain(
            blockCount == 0 ? "no codec setup data" : "exactly an SPS followed by a PPS");
    }

    [Fact]
    public void ValidateCodecSpecificData_RejectsReorderedParameterSets()
    {
        byte[][] blocks =
        [
            [0, 0, 0, 1, 0x68, 0xee],
            [0, 0, 0, 1, 0x67, 0x64],
        ];

        Action action = () => NativeMirrorEngine.ValidateCodecSpecificData(VideoCodec.H264, blocks);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("SPS");
    }

    [Fact]
    public void ValidateCodecSpecificData_RejectsAParameterSetWithoutAnnexBFraming()
    {
        byte[][] blocks =
        [
            [0x67, 0x64, 0x00, 0x1f],
            [0, 0, 0, 1, 0x68, 0xee],
        ];

        Action action = () => NativeMirrorEngine.ValidateCodecSpecificData(VideoCodec.H264, blocks);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("Annex-B");
    }

    [Fact]
    public void ValidateCodecSpecificData_RejectsAnEmptyParameterSetNal()
    {
        byte[][] blocks =
        [
            [0, 0, 0, 1, 0x67],
            [0, 0, 0, 1, 0x68, 0xee],
        ];

        Action action = () => NativeMirrorEngine.ValidateCodecSpecificData(VideoCodec.H264, blocks);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("complete Annex-B NAL unit");
    }

    [Fact]
    public void ValidateCodecSpecificData_RejectsACombinedParameterSetStream()
    {
        byte[][] blocks =
        [
            [0, 0, 0, 1, 0x67, 0x64, 0, 0, 1, 0x68, 0xee],
            [0, 0, 0, 1, 0x68, 0xee],
        ];

        Action action = () => NativeMirrorEngine.ValidateCodecSpecificData(VideoCodec.H264, blocks);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("more than one NAL unit");
    }

    [Fact]
    public void ToMirrorStats_RejectsGeometryThatCannotConfigureAChromaSubsampledDecoder()
    {
        var stats = ValidStats();
        stats.Width = 1279;

        Action action = () => _ = NativeMirrorEngine.ToMirrorStats(FlintStatus.Ok, stats);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("invalid encoded dimensions");
    }

    [Fact]
    public void NativeStatusFailure_IsNotMistakenForValidZeroedOutput()
    {
        Action action = () => _ = NativeMirrorEngine.ToMirrorStats(FlintStatus.PlatformError, default);

        Should.Throw<MirrorEngineException>(action).Message.ShouldContain("Windows refused");
    }

    /// <summary>Whether the engine binary is present and reports a compatible version.</summary>
    private static bool EngineIsLoadable() => new NativeEngineProbeApi().TryProbe(out _);

    /// <summary>
    /// Starts a real session on this machine, when it has a screen and an encoder.
    /// </summary>
    /// <remarks>
    /// Skips rather than fails on a machine with no interactive desktop, which is what a build
    /// agent is. The layout and wording tests above are the ones that run everywhere.
    /// </remarks>
    [Fact]
    public void Start_OnAHostThatCanMirror_ReportsAnEvenCappedFrameSizeAndItsCodecData()
    {
        var options = new MirrorSessionOptions(MaxWidth: 1280);

        IMirrorEngineSession session;
        try
        {
            session = new NativeMirrorEngine().Start(options);
        }
        catch (MirrorEngineException)
        {
            return;
        }

        using (session)
        {
            session.Width.ShouldBeGreaterThan(0);
            session.Width.ShouldBeLessThanOrEqualTo((int)options.MaxWidth);
            (session.Width % 2).ShouldBe(0, "chroma subsampling has no representation for an odd edge");
            (session.Height % 2).ShouldBe(0);

            // A receiver cannot configure its decoder without these, so an empty list would be a
            // session that can never produce a picture.
            session.CodecSpecificData.ShouldNotBeEmpty();
            session.CodecSpecificData.ShouldAllBe(block => block.Length > 0);
        }
    }

    [Fact]
    public void Dispose_CalledTwice_IsSafeBecauseADoubleFreeCorruptsTheProcess()
    {
        IMirrorEngineSession session;
        try
        {
            session = new NativeMirrorEngine().Start(new MirrorSessionOptions());
        }
        catch (MirrorEngineException)
        {
            return;
        }

        session.Dispose();
        Should.NotThrow(session.Dispose);
    }

    [Fact]
    public void Next_AfterDispose_ThrowsRatherThanCallingThroughAReleasedHandle()
    {
        IMirrorEngineSession session;
        try
        {
            session = new NativeMirrorEngine().Start(new MirrorSessionOptions());
        }
        catch (MirrorEngineException)
        {
            return;
        }

        session.Dispose();

        Should.Throw<ObjectDisposedException>(() => session.Next(new byte[1024]));
        Should.Throw<ObjectDisposedException>(session.RequestKeyFrame);
        Should.Throw<ObjectDisposedException>(() => session.ReadStats());
    }

    [Fact]
    public void Next_WithABufferTooSmallForAnAccessUnit_SaysSoRatherThanSendingHalfAFrame()
    {
        // Half an access unit decodes to garbage, and a decoder fed garbage stays broken until the
        // next key frame — far from where the mistake was made.
        IMirrorEngineSession session;
        try
        {
            session = new NativeMirrorEngine().Start(new MirrorSessionOptions(MaxWidth: 640));
        }
        catch (MirrorEngineException)
        {
            return;
        }

        using (session)
        {
            var tiny = new byte[1];
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    session.Next(tiny);
                }
                catch (MirrorEngineException exception)
                {
                    exception.Message.ShouldContain("did not fit");
                    return;
                }
            }

            // A completely still desktop can legitimately produce nothing at all, so never
            // reaching an access unit is not a failure of this behaviour.
        }
    }

    private static NativeMirrorFrame ValidFrame() => new()
    {
        Kind = (int)MirrorTickKind.Encoded,
        KeyFrame = 1,
        PresentationTimeUs = 33_333,
        ByteCount = 64,
    };

    private static NativeMirrorStats ValidStats() => new()
    {
        FramesEncoded = 3,
        FramesUnchanged = 4,
        Recoveries = 1,
        BytesEncoded = 1024,
        Width = 1280,
        Height = 720,
    };
}
