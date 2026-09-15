using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Flint.Core;

namespace Flint.Engine.Interop;

/// <summary>The production implementation of <see cref="IMirrorEngine"/>.</summary>
/// <remarks>
/// Composition only: every decision about what a tick means lives in the engine, because the
/// managed side must not be the thing standing between two frames.
/// </remarks>
public sealed partial class NativeMirrorEngine : IMirrorEngine
{
    /// <summary>The largest codec-data block the wire protocol can carry.</summary>
    private const uint MaxCodecDataBytes = 1024 * 1024;

    /// <summary>Every symbol that constitutes one complete mirror ABI.</summary>
    internal static IReadOnlyList<string> RequiredAbiExports { get; } =
    [
        "flint_mirror_start",
        "flint_mirror_next",
        "flint_mirror_codec_data",
        "flint_mirror_codec",
        "flint_mirror_request_key_frame",
        "flint_mirror_stats",
        "flint_mirror_stop",
    ];

    /// <inheritdoc />
    public IMirrorEngineSession Start(MirrorSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var config = new NativeMirrorConfig
        {
            OutputIndex = options.OutputIndex,
            FrameRate = options.FrameRate,
            BitrateBitsPerSecond = options.BitrateBitsPerSecond,
            MaxWidth = options.MaxWidth,
        };

        // The whole start path is guarded, not just the first call. A mismatch between this
        // wrapper and the engine binary can surface at any entry point the session touches while
        // describing itself, and every one of those means the same thing to a person: this build
        // cannot mirror.
        try
        {
            EnsureMirrorAbiPresent();

            nint handle;
            int status;
            unsafe
            {
                nint raw = 0;
                status = NativeMethods.Start(&config, &raw);
                handle = raw;
            }

            if ((FlintStatus)status is not FlintStatus.Ok)
            {
                throw new MirrorEngineException(DescribeStartFailure((FlintStatus)status));
            }

            if (handle == 0)
            {
                throw new MirrorEngineException(
                    "The Flint engine reported a successful mirror start without returning a session. "
                        + "This build's engine does not match the app.");
            }

            return new NativeMirrorEngineSession(handle);
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or MarshalDirectiveException)
        {
            throw new MirrorEngineException(
                "The Flint engine could not be loaded, so this PC cannot capture or encode a screen. "
                    + "This build's engine may not match the app.",
                exception);
        }
    }

    /// <summary>Turns a start status into something worth showing a person.</summary>
    /// <remarks>
    /// The engine reports "this machine cannot" and "this wrapper has a defect" as different codes,
    /// and they need different words: one is a fact about the hardware, the other is a bug.
    /// </remarks>
    internal static string DescribeStartFailure(FlintStatus status) => status switch
    {
        FlintStatus.PlatformError =>
            "Windows would not give Flint a screen to capture or an encoder to use. This happens "
                + "when another program already holds the screen capture, or when no H.264 encoder "
                + "is installed.",
        FlintStatus.InternalError =>
            "The Flint engine failed while starting the mirror session. This is a defect in Flint.",
        FlintStatus.InvalidArgument =>
            "The mirror settings are invalid. Frame rate and bitrate must be greater than zero, "
                + "and maximum width must be zero or at least two pixels.",
        _ => $"The Flint engine refused to start a mirror session (status {(int)status}).",
    };

    /// <summary>
    /// Rejects a partially updated engine before it acquires a display or starts an encoder.
    /// </summary>
    /// <remarks>
    /// The engine version predates this ABI and therefore cannot distinguish an old binary that
    /// has none of these functions from a current one. Resolving every export up front prevents a
    /// headless machine from validating only <c>flint_mirror_start</c> and discovering the next
    /// missing function only after a real user starts mirroring.
    /// </remarks>
    private static void EnsureMirrorAbiPresent()
    {
        if (!NativeLibrary.TryLoad(
                NativeEngineProbeApi.LibraryName,
                typeof(NativeMirrorEngine).Assembly,
                searchPath: null,
                out var library))
        {
            // Let the source-generated import produce the platform's normal DllNotFoundException,
            // which the caller-facing catch above translates consistently with the probe API.
            return;
        }

        try
        {
            foreach (var export in RequiredAbiExports)
            {
                if (!NativeLibrary.TryGetExport(library, export, out _))
                {
                    throw new EntryPointNotFoundException(
                        $"The Flint engine does not export {export}.");
                }
            }
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    /// <summary>Validates one native frame result before managed code trusts its lengths.</summary>
    internal static MirrorTick ToMirrorTick(FlintStatus status, NativeMirrorFrame frame, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        if (status is FlintStatus.BufferTooSmall)
        {
            throw new MirrorEngineException(
                $"An access unit of {frame.ByteCount} bytes did not fit the {capacity}-byte frame buffer.");
        }

        ThrowForStatus(status, "encoding a frame");

        if (!Enum.IsDefined(typeof(MirrorTickKind), frame.Kind))
        {
            throw new MirrorEngineException(
                $"The Flint engine returned an unknown mirror tick kind ({frame.Kind}).");
        }

        if (frame.KeyFrame > 1)
        {
            throw new MirrorEngineException(
                $"The Flint engine returned an invalid key-frame flag ({frame.KeyFrame}).");
        }

        var kind = (MirrorTickKind)frame.Kind;
        if (kind is MirrorTickKind.Encoded)
        {
            if (frame.ByteCount is 0 || frame.ByteCount > capacity)
            {
                throw new MirrorEngineException(
                    $"The Flint engine returned an invalid access-unit length ({frame.ByteCount}) "
                        + $"for a {capacity}-byte buffer.");
            }

            if (frame.PresentationTimeUs < 0)
            {
                throw new MirrorEngineException(
                    $"The Flint engine returned a negative presentation time ({frame.PresentationTimeUs}).");
            }
        }
        else if (frame.ByteCount != 0 || frame.KeyFrame != 0)
        {
            throw new MirrorEngineException(
                "The Flint engine attached encoded-frame data to a tick that produced no frame.");
        }

        return new MirrorTick(
            kind,
            checked((int)frame.ByteCount),
            frame.KeyFrame == 1,
            frame.PresentationTimeUs);
    }

    /// <summary>Validates the codec identifier returned by the native session.</summary>
    internal static VideoCodec ToVideoCodec(FlintStatus status, uint codec)
    {
        ThrowForStatus(status, "reporting the encoded codec");
        if (codec > int.MaxValue || !Enum.IsDefined(typeof(VideoCodec), (int)codec))
        {
            throw new MirrorEngineException(
                $"The Flint engine returned an unknown video codec ({codec}).");
        }

        return (VideoCodec)codec;
    }

    /// <summary>Rejects codec setup data that cannot configure the advertised decoder.</summary>
    internal static void ValidateCodecSpecificData(
        VideoCodec codec,
        IReadOnlyList<byte[]> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (blocks.Count == 0)
        {
            throw new MirrorEngineException(
                "The mirror session returned no codec setup data, so the receiver could not "
                    + "configure its decoder.");
        }

        if (codec is not VideoCodec.H264)
        {
            return;
        }

        // The native H.264 contract is deliberately narrower than arbitrary Annex B: Android is
        // configured with csd-0 = SPS and csd-1 = PPS. Accept either legal start-code width, but
        // never forward a combined stream, an extra parameter set, or reordered setup data.
        if (blocks.Count != 2)
        {
            throw new MirrorEngineException(
                $"The H.264 encoder returned {blocks.Count} codec-data blocks; exactly an SPS "
                    + "followed by a PPS is required.");
        }

        ValidateH264ParameterSet(blocks[0], expectedNalType: 7, "SPS");
        ValidateH264ParameterSet(blocks[1], expectedNalType: 8, "PPS");
    }

    /// <summary>Validates one independently framed H.264 parameter-set NAL unit.</summary>
    private static void ValidateH264ParameterSet(
        byte[] block,
        byte expectedNalType,
        string name)
    {
        if (block is null)
        {
            throw new MirrorEngineException($"The H.264 {name} codec-data block was null.");
        }

        var startCodeLength = block.AsSpan() switch
        {
            [0, 0, 0, 1, ..] => 4,
            [0, 0, 1, ..] => 3,
            _ => 0,
        };

        // A NAL header without any RBSP bytes is not a usable parameter set.
        if (startCodeLength == 0 || block.Length <= startCodeLength + 1)
        {
            throw new MirrorEngineException(
                $"The H.264 {name} codec-data block is not a complete Annex-B NAL unit.");
        }

        var nalHeader = block[startCodeLength];
        if ((nalHeader & 0x80) != 0 || (nalHeader & 0x1f) != expectedNalType)
        {
            throw new MirrorEngineException(
                $"The H.264 {name} codec-data block has the wrong NAL-unit type.");
        }

        for (var index = startCodeLength + 1; index + 2 < block.Length; index++)
        {
            if (block[index] == 0
                && block[index + 1] == 0
                && (block[index + 2] == 1
                    || (index + 3 < block.Length
                        && block[index + 2] == 0
                        && block[index + 3] == 1)))
            {
                throw new MirrorEngineException(
                    $"The H.264 {name} codec-data block contains more than one NAL unit.");
            }
        }
    }

    /// <summary>Validates and converts counters and encoded geometry from the native session.</summary>
    internal static (MirrorSessionStats Stats, int Width, int Height) ToMirrorStats(
        FlintStatus status,
        NativeMirrorStats native)
    {
        ThrowForStatus(status, "reporting mirror counters");

        if (native.Width is 0 || native.Height is 0
            || native.Width > int.MaxValue || native.Height > int.MaxValue
            || (native.Width & 1) != 0 || (native.Height & 1) != 0)
        {
            throw new MirrorEngineException(
                $"The Flint engine returned invalid encoded dimensions ({native.Width}x{native.Height}).");
        }

        if (native.FramesEncoded > long.MaxValue
            || native.FramesUnchanged > long.MaxValue
            || native.Recoveries > long.MaxValue
            || native.BytesEncoded > long.MaxValue)
        {
            throw new MirrorEngineException("The Flint engine returned mirror counters outside the managed range.");
        }

        return (
            new MirrorSessionStats(
                (long)native.FramesEncoded,
                (long)native.FramesUnchanged,
                (long)native.Recoveries,
                (long)native.BytesEncoded),
            (int)native.Width,
            (int)native.Height);
    }

    /// <summary>Turns a failed native operation into one consistent managed exception.</summary>
    private static void ThrowForStatus(FlintStatus status, string operation)
    {
        if (status is FlintStatus.Ok)
        {
            return;
        }

        throw new MirrorEngineException(
            status switch
            {
                FlintStatus.PlatformError => $"Windows refused while the Flint engine was {operation}.",
                FlintStatus.InternalError => $"The Flint engine failed internally while {operation}.",
                FlintStatus.InvalidArgument =>
                    $"The Flint app supplied invalid data while {operation} (status {(int)status}).",
                FlintStatus.NullArgument or FlintStatus.BufferTooSmall =>
                    $"The Flint app and engine disagreed while {operation} (status {(int)status}).",
                _ => $"The Flint engine failed while {operation} (status {(int)status}).",
            });
    }

    private static partial class NativeMethods
    {
        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_mirror_start")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Start(NativeMirrorConfig* config, nint* handle);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_mirror_next")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Next(
            nint handle,
            byte* buffer,
            uint capacity,
            NativeMirrorFrame* frame);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_mirror_codec_data")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int CodecData(
            nint handle,
            uint index,
            byte* buffer,
            uint capacity,
            uint* length);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_mirror_codec")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Codec(nint handle, uint* codec);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_mirror_request_key_frame")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int RequestKeyFrame(nint handle);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_mirror_stats")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static unsafe partial int Stats(nint handle, NativeMirrorStats* stats);

        [LibraryImport(NativeEngineProbeApi.LibraryName, EntryPoint = "flint_mirror_stop")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        internal static partial int Stop(nint handle);
    }

    /// <summary>One live native session.</summary>
    private sealed class NativeMirrorEngineSession : IMirrorEngineSession
    {
        /// <summary>
        /// Blocks of codec setup data the engine may publish.
        /// </summary>
        /// <remarks>
        /// H.264 publishes two — SPS and PPS. The cap bounds the walk so a malformed engine cannot
        /// spin this loop, and matches what the wire format is willing to carry.
        /// </remarks>
        private const int MaxCodecDataBlocks = 16;

        private nint handle;

        internal NativeMirrorEngineSession(nint handle)
        {
            this.handle = handle;
            try
            {
                NativeMirrorStats nativeStats;
                int statsStatus;
                unsafe
                {
                    statsStatus = NativeMethods.Stats(handle, &nativeStats);
                }

                var initial = ToMirrorStats((FlintStatus)statsStatus, nativeStats);
                Width = initial.Width;
                Height = initial.Height;

                uint nativeCodec;
                int codecStatus;
                unsafe
                {
                    codecStatus = NativeMethods.Codec(handle, &nativeCodec);
                }

                Codec = ToVideoCodec((FlintStatus)codecStatus, nativeCodec);
                var codecSpecificData = ReadCodecData(handle);
                ValidateCodecSpecificData(Codec, codecSpecificData);
                CodecSpecificData = codecSpecificData;
            }
            catch
            {
                // Construction owns the handle as soon as it is stored. Any metadata failure must
                // release capture and the encoder before it escapes.
                Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public VideoCodec Codec { get; }

        /// <inheritdoc />
        public int Width { get; }

        /// <inheritdoc />
        public int Height { get; }

        /// <inheritdoc />
        public IReadOnlyList<byte[]> CodecSpecificData { get; }

        /// <inheritdoc />
        public MirrorTick Next(Span<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            if (buffer.IsEmpty)
            {
                throw new ArgumentException("The frame buffer must not be empty.", nameof(buffer));
            }

            NativeMirrorFrame frame;
            int status;
            try
            {
                unsafe
                {
                    fixed (byte* pointer = buffer)
                    {
                        status = NativeMethods.Next(handle, pointer, (uint)buffer.Length, &frame);
                    }
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or MarshalDirectiveException)
            {
                throw AbiCallFailed(exception);
            }

            return ToMirrorTick((FlintStatus)status, frame, buffer.Length);
        }

        /// <inheritdoc />
        public void RequestKeyFrame()
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            try
            {
                ThrowForStatus(
                    (FlintStatus)NativeMethods.RequestKeyFrame(handle),
                    "requesting a key frame");
            }
            catch (Exception exception) when (exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or MarshalDirectiveException)
            {
                throw AbiCallFailed(exception);
            }
        }

        /// <inheritdoc />
        public MirrorSessionStats ReadStats()
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);

            NativeMirrorStats native;
            int status;
            try
            {
                unsafe
                {
                    status = NativeMethods.Stats(handle, &native);
                }
            }
            catch (Exception exception) when (exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or MarshalDirectiveException)
            {
                throw AbiCallFailed(exception);
            }

            var converted = ToMirrorStats((FlintStatus)status, native);
            if (converted.Width != Width || converted.Height != Height)
            {
                throw new MirrorEngineException(
                    $"The Flint engine changed its encoded dimensions from {Width}x{Height} to "
                        + $"{converted.Width}x{converted.Height} without a new decoder configuration.");
            }

            return converted.Stats;
        }

        public void Dispose()
        {
            // Cleared before the call so a second dispose is a no-op rather than a double free,
            // which is the one mistake at this boundary that corrupts the process rather than
            // throwing.
            var owned = handle;
            handle = 0;
            if (owned != 0)
            {
                try
                {
                    // Teardown is best effort. There is no safe retry once ownership has been
                    // surrendered, and throwing from Dispose would hide the capture/encode error
                    // that caused the teardown in the first place.
                    _ = NativeMethods.Stop(owned);
                }
                catch (Exception exception) when (exception is DllNotFoundException
                    or EntryPointNotFoundException
                    or BadImageFormatException
                    or MarshalDirectiveException)
                {
                }
            }
        }

        private static IReadOnlyList<byte[]> ReadCodecData(nint handle)
        {
            var blocks = new List<byte[]>(2);
            for (var index = 0u; index < MaxCodecDataBlocks; index++)
            {
                uint length;
                unsafe
                {
                    // A zero-capacity call asks for the size without committing to a buffer.
                    var probe = NativeMethods.CodecData(handle, index, null, 0, &length);
                    if ((FlintStatus)probe is not (FlintStatus.Ok or FlintStatus.BufferTooSmall))
                    {
                        ThrowForStatus((FlintStatus)probe, "reading codec setup data");
                    }
                }

                if (length == 0)
                {
                    break;
                }

                if (length > MaxCodecDataBytes)
                {
                    throw new MirrorEngineException(
                        $"The Flint engine returned an oversized codec-data block ({length} bytes).");
                }

                var block = new byte[length];
                unsafe
                {
                    fixed (byte* pointer = block)
                    {
                        var expectedLength = length;
                        var copy = (FlintStatus)NativeMethods.CodecData(
                            handle,
                            index,
                            pointer,
                            expectedLength,
                            &length);
                        ThrowForStatus(copy, "copying codec setup data");
                        if (length != expectedLength)
                        {
                            throw new MirrorEngineException(
                                "The Flint engine changed a codec-data block's size while it was being copied.");
                        }
                    }
                }

                blocks.Add(block);
            }

            return blocks;
        }

        private static MirrorEngineException AbiCallFailed(Exception innerException) =>
            new(
                "The Flint engine's mirror ABI became unavailable while a session was running. "
                    + "This build's engine may not match the app.",
                innerException);
    }
}
