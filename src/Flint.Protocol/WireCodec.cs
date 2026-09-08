using System.Buffers.Binary;
using System.Text;

namespace Flint.Protocol;

/// <summary>
/// Encodes and decodes the REX wire format.
/// </summary>
/// <remarks>
/// <para>
/// A frame is a four-byte big-endian body length followed by an eight-byte envelope — magic,
/// protocol version, message type, flags — and a payload. Every integer is big-endian, because
/// Java's <c>DataOutputStream</c> is and the Kotlin implementation came first.
/// </para>
/// <para>
/// The decoder is deliberately strict. A trailing byte, a malformed UTF-8 sequence, or a boolean
/// that is not exactly zero or one is an error rather than something to shrug at, because a frame
/// arrives from an unauthenticated peer on the local network and a lenient parser is where such
/// peers get their leverage.
/// </para>
/// </remarks>
public static class WireCodec
{
    /// <summary>The envelope that precedes every payload: magic, version, type, flags.</summary>
    public const int EnvelopeLength = 8;

    /// <summary>
    /// The largest frame body the decoder will accept.
    /// </summary>
    /// <remarks>
    /// A peer can declare any length. Capping it is what stops a hostile or broken responder on the
    /// local network making the host allocate without bound.
    /// </remarks>
    public const int MaxFrameLength = 16 * 1024 * 1024;

    private const int Magic = 0x5243;

    private const int MaxDeviceNameBytes = 255;
    private const int MaxFingerprintBytes = 512;
    private const int MaxAuthBytes = 4 * 1024;
    private const int MaxCodecConfigBytes = 1024 * 1024;
    private const int MaxMediaPacketBytes = 15 * 1024 * 1024;
    private const int MaxTextControlBytes = 16 * 1024;
    private const int MaxByeDetailBytes = 1024;
    private const int MaxUrlBytes = 4 * 1024;
    private const int MaxTitleBytes = 512;
    private const int MaxCodecCapabilities = 256;
    private const int MaxCodecConfigBlocks = 16;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>Encodes a frame, envelope and all.</summary>
    /// <exception cref="WireFormatException">A field exceeds its cap or violates an invariant.</exception>
    public static byte[] Encode(WireFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        var payload = EncodePayload(frame.ProtocolVersion, frame.Message);
        var bodyLength = EnvelopeLength + payload.Length;
        if (bodyLength > MaxFrameLength)
        {
            throw new WireFormatException($"Frame is too large: {bodyLength} bytes.");
        }

        var buffer = new byte[4 + bodyLength];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteInt32BigEndian(span[..4], bodyLength);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..6], Magic);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..8], (ushort)frame.ProtocolVersion);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..10], (ushort)frame.Message.TypeId);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..12], (ushort)frame.Flags);
        payload.CopyTo(span[12..]);
        return buffer;
    }

    /// <summary>Decodes exactly one frame, rejecting anything left over.</summary>
    /// <exception cref="WireFormatException">The buffer is not exactly one well-formed frame.</exception>
    public static WireFrame Decode(ReadOnlySpan<byte> bytes)
    {
        var frame = DecodePrefix(bytes, out var consumed);
        if (consumed != bytes.Length)
        {
            throw new WireFormatException($"Trailing bytes after frame: {bytes.Length - consumed}.");
        }

        return frame;
    }

    /// <summary>
    /// Decodes the first frame in a buffer, reporting how many bytes it used.
    /// </summary>
    /// <remarks>
    /// The entry point for a stream reader: the caller keeps the remainder and calls again. A
    /// partial frame throws <see cref="WireTruncatedException"/>, which a stream reader treats as
    /// "read more" rather than as a failure.
    /// </remarks>
    public static WireFrame DecodePrefix(ReadOnlySpan<byte> bytes, out int consumed)
    {
        if (bytes.Length < 4)
        {
            throw new WireTruncatedException("frame length");
        }

        var bodyLength = BinaryPrimitives.ReadInt32BigEndian(bytes[..4]);
        if (bodyLength < EnvelopeLength || bodyLength > MaxFrameLength)
        {
            throw new WireFormatException($"Invalid frame body length: {bodyLength}.");
        }

        var total = 4 + bodyLength;
        if (bytes.Length < total)
        {
            throw new WireTruncatedException("frame body");
        }

        var body = bytes[4..total];
        var magic = BinaryPrimitives.ReadUInt16BigEndian(body[..2]);
        if (magic != Magic)
        {
            throw new WireFormatException($"Invalid frame magic: 0x{magic:x4}.");
        }

        var protocolVersion = BinaryPrimitives.ReadUInt16BigEndian(body[2..4]);
        if (protocolVersion == 0)
        {
            throw new WireFormatException("Protocol version 0 is invalid.");
        }

        var typeId = BinaryPrimitives.ReadUInt16BigEndian(body[4..6]);
        if (typeId == 0)
        {
            throw new WireFormatException("Message type 0 is invalid.");
        }

        var flags = BinaryPrimitives.ReadUInt16BigEndian(body[6..8]);
        var message = DecodePayload(protocolVersion, typeId, body[EnvelopeLength..]);

        consumed = total;
        return new WireFrame(protocolVersion, message, flags);
    }

    private static void RequireSupported(int protocolVersion)
    {
        if (protocolVersion is < ProtocolVersion.MinSupported or > ProtocolVersion.Current)
        {
            throw new UnsupportedProtocolVersionException(protocolVersion);
        }
    }

    private static byte[] EncodePayload(int protocolVersion, WireMessage message)
    {
        // An unknown message is relayed byte for byte, and is deliberately not version-checked:
        // this build cannot judge a payload it does not understand, and refusing to carry it would
        // break forward compatibility for no gain.
        if (message is UnknownMessage unknown)
        {
            return unknown.Payload.ToArray();
        }

        RequireSupported(protocolVersion);
        if (!BrowserWireRules.IsAllowedAtVersion(protocolVersion, message))
        {
            var requiredVersion = BrowserWireRules.IsWorkspaceMessage(message) ? 3 : 2;
            throw new WireFormatException(
                $"Browser protocol values require version {requiredVersion}; frame declared version {protocolVersion}.");
        }

        if (BrowserWireRules.IsBrowserMessage(message))
        {
            return BrowserWireCodec.Encode(message);
        }

        var writer = new PayloadWriter();

        switch (message)
        {
            case HelloMessage hello:
                Require(hello.MinimumVersion >= 1 && hello.MaximumVersion >= hello.MinimumVersion,
                    "Hello version range is inverted.");
                Require(!string.IsNullOrEmpty(hello.DeviceName), "Hello device name is empty.");
                Require(hello.ScreenWidth > 0 && hello.ScreenHeight > 0 && hello.DensityDpi > 0,
                    "Hello screen geometry must be positive.");
                Require(hello.CodecCapabilities.Length <= MaxCodecCapabilities,
                    $"Too many codec capabilities: {hello.CodecCapabilities.Length}.");

                writer.UInt16(hello.MinimumVersion);
                writer.UInt16(hello.MaximumVersion);
                writer.Utf8UInt16(hello.DeviceName, MaxDeviceNameBytes, "device name");

                var codecs = hello.NormalisedCodecs;
                writer.UInt16(codecs.Length);
                foreach (var codec in codecs)
                {
                    writer.UInt16(codec.Value);
                }

                writer.Int32(hello.ScreenWidth);
                writer.Int32(hello.ScreenHeight);
                writer.Int32(hello.DensityDpi);
                break;

            case AuthMessage auth:
                Require(auth.Credential.Length > 0, "Auth credential is empty.");
                writer.UInt8((int)auth.Method);
                writer.Binary(auth.Credential, MaxAuthBytes, "credential");
                if (auth.PublicKeyFingerprint is { } fingerprint)
                {
                    Require(!string.IsNullOrWhiteSpace(fingerprint), "Auth fingerprint is blank.");
                    writer.UInt8(1);
                    writer.Utf8UInt16(fingerprint, MaxFingerprintBytes, "fingerprint");
                }
                else
                {
                    writer.UInt8(0);
                }

                break;

            case VideoConfigMessage video:
                Require(video.Width > 0 && video.Height > 0, "Video dimensions must be positive.");
                Require(video.CodecSpecificData.Length <= MaxCodecConfigBlocks,
                    $"Too many video config blocks: {video.CodecSpecificData.Length}.");
                writer.UInt16(video.Codec.Value);
                writer.Int32(video.Width);
                writer.Int32(video.Height);
                writer.UInt8(video.CodecSpecificData.Length);
                foreach (var block in video.CodecSpecificData)
                {
                    writer.Binary(block, MaxCodecConfigBytes, "video config block");
                }

                break;

            case VideoPacket packet:
                Require(packet.PresentationTimeUs >= 0, "Presentation time is negative.");
                Require(packet.Data.Length > 0, "Video packet is empty.");
                writer.Int64(packet.PresentationTimeUs);
                writer.UInt8(packet.KeyFrame ? 1 : 0);
                writer.Binary(packet.Data, MaxMediaPacketBytes, "video packet");
                break;

            case AudioConfigMessage audio:
                Require(audio.SampleRateHz is >= 1 and <= 768_000, "Sample rate is out of range.");
                Require(audio.ChannelCount is >= 1 and <= 32, "Channel count is out of range.");
                writer.UInt16(audio.Codec.Value);
                writer.Int32(audio.SampleRateHz);
                writer.UInt8(audio.ChannelCount);
                writer.Binary(audio.CodecSpecificData, MaxCodecConfigBytes, "audio config");
                break;

            case AudioPacket packet:
                Require(packet.PresentationTimeUs >= 0, "Presentation time is negative.");
                Require(packet.Data.Length > 0, "Audio packet is empty.");
                writer.Int64(packet.PresentationTimeUs);
                writer.Binary(packet.Data, MaxMediaPacketBytes, "audio packet");
                break;

            case ControlMessage control:
                Require(control.SequenceNumber >= 0, "Control sequence is negative.");
                writer.Int64(control.SequenceNumber);
                writer.UInt8(control.Event.EventId);
                EncodeControl(writer, control.Event);
                break;

            case StatsMessage stats:
                Require(
                    stats.ReceiverQueueDepth >= 0
                        && stats.DecodeLatencyUs >= 0
                        && stats.RoundTripTimeUs >= 0
                        && stats.DroppedVideoFrames >= 0,
                    "Stats counters must not be negative.");
                writer.Int32(stats.ReceiverQueueDepth);
                writer.Int64(stats.DecodeLatencyUs);
                writer.Int64(stats.RoundTripTimeUs);
                writer.Int64(stats.DroppedVideoFrames);
                break;

            case ByeMessage bye:
                writer.UInt8((int)bye.Reason);
                writer.Utf8UInt16(bye.Detail, MaxByeDetailBytes, "bye detail");
                break;

            case MediaCommandMessage media:
                Require(media.DurationMs >= -1, "Media duration must be -1 or greater.");
                Require(media.StartPositionMs >= 0, "Media start position is negative.");
                // An empty Url is valid: it means "play what was just pushed via MediaDataMessage",
                // not "fetch from the network". Only the MIME type is mandatory for a load.
                Require(media.Action != MediaAction.Load || !string.IsNullOrWhiteSpace(media.MimeType),
                    "Media load requires a MIME type.");
                Require(media.SubtitleUrl is null || !string.IsNullOrWhiteSpace(media.SubtitleUrl),
                    "Media subtitle URL is blank.");
                writer.UInt8((int)media.Action);
                writer.Utf8UInt16(media.Url, MaxUrlBytes, "media URL");
                writer.Utf8UInt16(media.Title, MaxTitleBytes, "media title");
                writer.Utf8UInt16(media.MimeType, MaxTitleBytes, "media MIME type");
                writer.Int64(media.DurationMs);
                writer.Int64(media.StartPositionMs);
                writer.NullableUtf8UInt16(media.SubtitleUrl, MaxUrlBytes, "media subtitle URL");
                break;

            case MediaDataMessage chunk:
                writer.UInt8(chunk.IsFinal ? 1 : 0);
                writer.Binary(chunk.Data, MaxMediaPacketBytes, "media data chunk");
                break;

            case SurfaceMessage surface:
                if (surface.Mode == SurfaceMode.Browser && protocolVersion < 2)
                {
                    throw new WireFormatException("Browser surface requires protocol version 2.");
                }

                writer.UInt8((int)surface.Mode);
                writer.Utf8UInt16(surface.Caption, MaxTitleBytes, "surface caption");
                break;

            case PlaybackStateMessage playback:
                Require(playback.PositionMs >= 0, "Playback position is negative.");
                Require(playback.DurationMs >= -1, "Playback duration must be -1 or greater.");
                writer.UInt8((int)playback.State);
                writer.Int64(playback.PositionMs);
                writer.Int64(playback.DurationMs);
                writer.Utf8UInt16(playback.Detail, MaxByeDetailBytes, "playback detail");
                break;

            default:
                throw new WireFormatException($"Unhandled message type: {message.GetType().Name}.");
        }

        return writer.ToArray();
    }

    private static void EncodeControl(PayloadWriter writer, ControlEvent controlEvent)
    {
        switch (controlEvent)
        {
            case TransportControl transport:
                var seeking = transport.Action is TransportAction.SeekTo;
                Require(!seeking || transport.PositionMs >= 0, "Seek requires a position.");
                Require(seeking || transport.PositionMs == -1, "Only seek carries a position.");
                writer.UInt8((int)transport.Action);
                writer.Int64(transport.PositionMs);
                break;

            case PointerControl pointer:
                Require(float.IsFinite(pointer.X) && float.IsFinite(pointer.Y),
                    "Pointer coordinates must be finite.");
                writer.UInt8((int)pointer.Action);
                writer.Single(pointer.X);
                writer.Single(pointer.Y);
                writer.Int32(pointer.Buttons);
                break;

            case KeyControl key:
                Require(key.KeyCode >= 0, "Key code is negative.");
                writer.UInt8((int)key.Action);
                writer.Int32(key.KeyCode);
                break;

            case TextControl text:
                writer.Utf8Int32(text.Text, MaxTextControlBytes, "text input");
                break;

            case VolumeControl volume:
                Require(float.IsFinite(volume.Level) && volume.Level is >= 0.0f and <= 1.0f,
                    "Volume must be between 0 and 1.");
                writer.Single(volume.Level);
                break;

            default:
                throw new WireFormatException($"Unhandled control event: {controlEvent.GetType().Name}.");
        }
    }

    private static WireMessage DecodePayload(int protocolVersion, int typeId, ReadOnlySpan<byte> payload)
    {
        if (!Enum.IsDefined(typeof(WireMessageType), typeId))
        {
            // Forward compatibility: carry what we cannot read.
            return new UnknownMessage(typeId, BinaryData.From(payload));
        }

        RequireSupported(protocolVersion);
        if (BrowserWireRules.IsBrowserType(typeId))
        {
            if (protocolVersion < 2)
            {
                throw new WireFormatException("Browser message types require protocol version 2.");
            }

            if (typeId >= 35 && protocolVersion < 4) throw new WireFormatException("Workspace resizing requires protocol version 4.");
            return BrowserWireCodec.Decode((WireMessageType)typeId, payload);
        }

        var reader = new PayloadReader(payload);

        WireMessage message = (WireMessageType)typeId switch
        {
            WireMessageType.Hello => DecodeHello(ref reader),
            WireMessageType.Auth => DecodeAuth(ref reader),
            WireMessageType.VideoConfig => DecodeVideoConfig(ref reader),
            WireMessageType.Video => new VideoPacket(
                reader.Int64("video presentation time"),
                reader.Boolean("key-frame flag"),
                reader.Binary(MaxMediaPacketBytes, "video packet")),
            WireMessageType.AudioConfig => DecodeAudioConfig(ref reader),
            WireMessageType.Audio => new AudioPacket(
                reader.Int64("audio presentation time"),
                reader.Binary(MaxMediaPacketBytes, "audio packet")),
            WireMessageType.Control => DecodeControl(ref reader),
            WireMessageType.Stats => new StatsMessage(
                reader.Int32("receiver queue depth"),
                reader.Int64("decode latency"),
                reader.Int64("round-trip time"),
                reader.Int64("dropped video frames")),
            WireMessageType.Bye => DecodeBye(ref reader),
            WireMessageType.MediaCommand => DecodeMediaCommand(ref reader),
            WireMessageType.MediaData => DecodeMediaData(ref reader),
            WireMessageType.Surface => DecodeSurface(protocolVersion, ref reader),
            WireMessageType.PlaybackState => DecodePlaybackState(ref reader),
            _ => throw new WireFormatException($"Unhandled message type: {typeId}."),
        };

        reader.RequireFinished();
        return message;
    }

    private static HelloMessage DecodeHello(ref PayloadReader reader)
    {
        var minimumVersion = reader.UInt16("minimum version");
        var maximumVersion = reader.UInt16("maximum version");
        var deviceName = reader.Utf8UInt16(MaxDeviceNameBytes, "device name");

        var count = reader.UInt16("codec count");
        if (count > MaxCodecCapabilities)
        {
            throw new WireFormatException($"Too many codec capabilities: {count}.");
        }

        var codecs = new List<CodecId>(count);
        for (var index = 0; index < count; index++)
        {
            var codec = new CodecId(reader.UInt16("codec id"));
            if (!codec.IsValid)
            {
                throw new WireFormatException("Invalid codec id: 0.");
            }

            codecs.Add(codec);
        }

        return new HelloMessage(
            minimumVersion,
            maximumVersion,
            deviceName,
            ValueList<CodecId>.From(codecs),
            reader.Int32("screen width"),
            reader.Int32("screen height"),
            reader.Int32("density dpi"));
    }

    private static AuthMessage DecodeAuth(ref PayloadReader reader)
    {
        var methodId = reader.UInt8("auth method");
        if (!Enum.IsDefined(typeof(AuthMethod), methodId))
        {
            throw new WireFormatException($"Unknown auth method: {methodId}.");
        }

        var credential = reader.Binary(MaxAuthBytes, "credential");
        var present = reader.Boolean("fingerprint presence");
        var fingerprint = present ? reader.Utf8UInt16(MaxFingerprintBytes, "fingerprint") : null;
        return new AuthMessage((AuthMethod)methodId, credential, fingerprint);
    }

    private static VideoConfigMessage DecodeVideoConfig(ref PayloadReader reader)
    {
        var codec = new CodecId(reader.UInt16("video codec"));
        if (!codec.IsValid)
        {
            throw new WireFormatException("Invalid video codec: 0.");
        }

        var width = reader.Int32("video width");
        var height = reader.Int32("video height");
        var count = reader.UInt8("video config count");
        if (count > MaxCodecConfigBlocks)
        {
            throw new WireFormatException($"Too many video config blocks: {count}.");
        }

        var blocks = new List<BinaryData>(count);
        for (var index = 0; index < count; index++)
        {
            blocks.Add(reader.Binary(MaxCodecConfigBytes, "video config block"));
        }

        return new VideoConfigMessage(codec, width, height, ValueList<BinaryData>.From(blocks));
    }

    private static AudioConfigMessage DecodeAudioConfig(ref PayloadReader reader)
    {
        var codec = new CodecId(reader.UInt16("audio codec"));
        if (!codec.IsValid)
        {
            throw new WireFormatException("Invalid audio codec: 0.");
        }

        return new AudioConfigMessage(
            codec,
            reader.Int32("sample rate"),
            reader.UInt8("channel count"),
            reader.Binary(MaxCodecConfigBytes, "audio config"));
    }

    private static ByeMessage DecodeBye(ref PayloadReader reader)
    {
        var reasonId = reader.UInt8("bye reason");
        if (!Enum.IsDefined(typeof(ByeReason), reasonId))
        {
            throw new WireFormatException($"Unknown bye reason: {reasonId}.");
        }

        return new ByeMessage((ByeReason)reasonId, reader.Utf8UInt16(MaxByeDetailBytes, "bye detail"));
    }

    private static MediaCommandMessage DecodeMediaCommand(ref PayloadReader reader)
    {
        var actionId = reader.UInt8("media action");
        if (!Enum.IsDefined(typeof(MediaAction), actionId))
        {
            throw new WireFormatException($"Unknown media action: {actionId}.");
        }

        return new MediaCommandMessage(
            (MediaAction)actionId,
            reader.Utf8UInt16(MaxUrlBytes, "media URL"),
            reader.Utf8UInt16(MaxTitleBytes, "media title"),
            reader.Utf8UInt16(MaxTitleBytes, "media MIME type"),
            reader.Int64("media duration"),
            reader.Int64("media start position"),
            reader.NullableUtf8UInt16(MaxUrlBytes, "media subtitle URL"));
    }

    private static MediaDataMessage DecodeMediaData(ref PayloadReader reader)
    {
        var isFinal = reader.Boolean("media data final flag");
        var data = reader.Binary(MaxMediaPacketBytes, "media data chunk");
        return new MediaDataMessage(data, isFinal);
    }

    private static SurfaceMessage DecodeSurface(int protocolVersion, ref PayloadReader reader)
    {
        var modeId = reader.UInt8("surface mode");
        if (!Enum.IsDefined(typeof(SurfaceMode), modeId))
        {
            throw new WireFormatException($"Unknown surface mode: {modeId}.");
        }

        var surface = new SurfaceMessage(
            (SurfaceMode)modeId,
            reader.Utf8UInt16(MaxTitleBytes, "surface caption"));
        if (surface.Mode == SurfaceMode.Browser && protocolVersion < 2)
        {
            throw new WireFormatException("Browser surface requires protocol version 2.");
        }

        return surface;
    }

    private static PlaybackStateMessage DecodePlaybackState(ref PayloadReader reader)
    {
        var stateId = reader.UInt8("playback state");
        if (!Enum.IsDefined(typeof(PlaybackState), stateId))
        {
            throw new WireFormatException($"Unknown playback state: {stateId}.");
        }

        var position = reader.Int64("playback position");
        var duration = reader.Int64("playback duration");
        if (position < 0 || duration < -1)
        {
            throw new WireFormatException("Playback progress is out of range.");
        }

        return new PlaybackStateMessage(
            (PlaybackState)stateId,
            position,
            duration,
            reader.Utf8UInt16(MaxByeDetailBytes, "playback detail"));
    }

    private static ControlMessage DecodeControl(ref PayloadReader reader)
    {
        var sequence = reader.Int64("control sequence");
        var eventId = reader.UInt8("control event type");

        ControlEvent controlEvent = eventId switch
        {
            1 => DecodeTransport(ref reader),
            2 => DecodePointer(ref reader),
            3 => DecodeKey(ref reader),
            4 => new TextControl(reader.Utf8Int32(MaxTextControlBytes, "text input")),
            5 => new VolumeControl(reader.Single("volume")),
            _ => throw new WireFormatException($"Unknown control event type: {eventId}."),
        };

        return new ControlMessage(sequence, controlEvent);
    }

    private static TransportControl DecodeTransport(ref PayloadReader reader)
    {
        var actionId = reader.UInt8("transport action");
        if (!Enum.IsDefined(typeof(TransportAction), actionId))
        {
            throw new WireFormatException($"Unknown transport action: {actionId}.");
        }

        return new TransportControl((TransportAction)actionId, reader.Int64("transport position"));
    }

    private static PointerControl DecodePointer(ref PayloadReader reader)
    {
        var actionId = reader.UInt8("pointer action");
        if (!Enum.IsDefined(typeof(PointerAction), actionId))
        {
            throw new WireFormatException($"Unknown pointer action: {actionId}.");
        }

        return new PointerControl(
            (PointerAction)actionId,
            reader.Single("pointer x"),
            reader.Single("pointer y"),
            reader.Int32("pointer buttons"));
    }

    private static KeyControl DecodeKey(ref PayloadReader reader)
    {
        var actionId = reader.UInt8("key action");
        if (!Enum.IsDefined(typeof(KeyAction), actionId))
        {
            throw new WireFormatException($"Unknown key action: {actionId}.");
        }

        return new KeyControl((KeyAction)actionId, reader.Int32("key code"));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new WireFormatException(message);
        }
    }

    internal sealed class PayloadWriter
    {
        private readonly List<byte> _bytes = [];

        internal void UInt8(int value)
        {
            if (value is < 0 or > 0xFF)
            {
                throw new WireFormatException($"Value is not a byte: {value}.");
            }

            _bytes.Add((byte)value);
        }

        internal void UInt16(int value)
        {
            if (value is < 0 or > 0xFFFF)
            {
                throw new WireFormatException($"Value is not a 16-bit unsigned integer: {value}.");
            }

            _bytes.Add((byte)(value >> 8));
            _bytes.Add((byte)value);
        }

        internal void Int32(int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        internal void Int64(long value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        internal void Single(float value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteSingleBigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        internal void Utf8UInt16(string value, int maximumBytes, string field)
        {
            var encoded = StrictUtf8.GetBytes(value);
            if (encoded.Length > maximumBytes || encoded.Length > 0xFFFF)
            {
                throw new WireFormatException($"The {field} is too long: {encoded.Length} bytes.");
            }

            UInt16(encoded.Length);
            _bytes.AddRange(encoded);
        }

        internal void Utf8Int32(string value, int maximumBytes, string field)
        {
            var encoded = StrictUtf8.GetBytes(value);
            if (encoded.Length > maximumBytes)
            {
                throw new WireFormatException($"The {field} is too long: {encoded.Length} bytes.");
            }

            Int32(encoded.Length);
            _bytes.AddRange(encoded);
        }

        internal void Utf8UInt32(string value, int maximumBytes, string field)
        {
            var encoded = StrictUtf8.GetBytes(value);
            if (encoded.Length > maximumBytes)
            {
                throw new WireFormatException($"The {field} is too long: {encoded.Length} bytes.");
            }

            UInt32((uint)encoded.Length);
            _bytes.AddRange(encoded);
        }

        internal void UInt32(uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            _bytes.AddRange(buffer);
        }

        internal void NullableUtf8UInt16(string? value, int maximumBytes, string field)
        {
            if (value is null)
            {
                UInt8(0);
                return;
            }

            UInt8(1);
            Utf8UInt16(value, maximumBytes, field);
        }

        internal void Binary(BinaryData value, int maximumBytes, string field)
        {
            if (value.Length > maximumBytes)
            {
                throw new WireFormatException($"The {field} is too long: {value.Length} bytes.");
            }

            Int32(value.Length);
            _bytes.AddRange(value.Span);
        }

        internal byte[] ToArray() => [.. _bytes];
    }

    internal ref struct PayloadReader(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;
        private int _offset = 0;

        private ReadOnlySpan<byte> Take(int count, string field)
        {
            if (count < 0 || _offset + count > _bytes.Length)
            {
                throw new WireTruncatedException(field);
            }

            var slice = _bytes.Slice(_offset, count);
            _offset += count;
            return slice;
        }

        internal int UInt8(string field) => Take(1, field)[0];

        internal int UInt16(string field) => BinaryPrimitives.ReadUInt16BigEndian(Take(2, field));

        internal int Int32(string field) => BinaryPrimitives.ReadInt32BigEndian(Take(4, field));

        internal long Int64(string field) => BinaryPrimitives.ReadInt64BigEndian(Take(8, field));

        internal float Single(string field) => BinaryPrimitives.ReadSingleBigEndian(Take(4, field));

        internal bool Boolean(string field) => UInt8(field) switch
        {
            0 => false,
            1 => true,
            var other => throw new WireFormatException($"Invalid {field}: {other}."),
        };

        internal BinaryData Binary(int maximumBytes, string field)
        {
            var length = Int32(field);
            if (length < 0 || length > maximumBytes)
            {
                throw new WireFormatException($"Invalid {field} length: {length}.");
            }

            return BinaryData.From(Take(length, field));
        }

        internal string Utf8UInt16(int maximumBytes, string field) =>
            Utf8(UInt16(field), maximumBytes, field);

        internal string Utf8Int32(int maximumBytes, string field)
        {
            var length = Int32(field);
            if (length < 0)
            {
                throw new WireFormatException($"Invalid {field} length: {length}.");
            }

            return Utf8(length, maximumBytes, field);
        }

        internal string Utf8UInt32(int maximumBytes, string field)
        {
            var length = UInt32(field);
            if (length > int.MaxValue)
            {
                throw new WireFormatException($"Invalid {field} length: {length}.");
            }

            return Utf8((int)length, maximumBytes, field);
        }

        internal uint UInt32(string field) => BinaryPrimitives.ReadUInt32BigEndian(Take(4, field));

        internal string? NullableUtf8UInt16(int maximumBytes, string field) =>
            Boolean($"{field} presence") ? Utf8UInt16(maximumBytes, field) : null;

        private string Utf8(int length, int maximumBytes, string field)
        {
            if (length > maximumBytes)
            {
                throw new WireFormatException($"Invalid {field} length: {length}.");
            }

            var slice = Take(length, field);
            try
            {
                // Strict: a malformed sequence throws rather than becoming a replacement character.
                // Silently substituting would let a peer smuggle a different string past a
                // comparison later.
                return StrictUtf8.GetString(slice);
            }
            catch (DecoderFallbackException)
            {
                throw new WireFormatException($"Invalid UTF-8 in {field}.");
            }
        }

        internal void RequireFinished()
        {
            var remaining = _bytes.Length - _offset;
            if (remaining != 0)
            {
                throw new WireFormatException($"Trailing payload bytes: {remaining}.");
            }
        }
    }
}
