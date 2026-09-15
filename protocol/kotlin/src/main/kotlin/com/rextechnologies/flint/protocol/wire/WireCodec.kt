package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.io.DataInputStream
import java.io.DataOutputStream
import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

open class WireFormatException(message: String, cause: Throwable? = null) : IOException(message, cause)

class UnsupportedProtocolVersionException(val version: Int) :
    WireFormatException("Unsupported wire payload version: $version")

/**
 * REX wire envelope.
 *
 * Every frame is a four-byte big-endian body length followed by:
 * magic (u16), protocol version (u16), message type (u16), flags (u16), payload.
 */
object WireCodec {
    const val MAX_FRAME_LENGTH: Int = 16 * 1024 * 1024
    private const val ENVELOPE_LENGTH = 8
    private const val MAGIC = 0x5243 // "RC"

    fun encode(frame: WireFrame): ByteArray {
        val payload = WireMessageCodec.encode(frame.protocolVersion, frame.message)
        val bodyLength = ENVELOPE_LENGTH.toLong() + payload.size
        if (bodyLength > MAX_FRAME_LENGTH) {
            throw WireFormatException("Frame is too large: $bodyLength bytes")
        }

        val output = ByteArrayOutputStream(4 + bodyLength.toInt())
        writeTo(output, frame, payload)
        return output.toByteArray()
    }

    fun writeTo(output: OutputStream, frame: WireFrame) {
        val payload = WireMessageCodec.encode(frame.protocolVersion, frame.message)
        writeTo(output, frame, payload)
    }

    private fun writeTo(output: OutputStream, frame: WireFrame, payload: ByteArray) {
        val bodyLength = ENVELOPE_LENGTH.toLong() + payload.size
        if (bodyLength > MAX_FRAME_LENGTH) {
            throw WireFormatException("Frame is too large: $bodyLength bytes")
        }
        val data = DataOutputStream(output)
        data.writeInt(bodyLength.toInt())
        data.writeShort(MAGIC)
        data.writeShort(frame.protocolVersion)
        data.writeShort(frame.message.typeId)
        data.writeShort(frame.flags)
        data.write(payload)
    }

    fun decode(encoded: ByteArray): WireFrame {
        val input = ByteArrayInputStream(encoded)
        val frame = readFrom(input) ?: throw WireFormatException("Missing frame")
        if (input.available() != 0) {
            throw WireFormatException("Trailing bytes after frame: ${input.available()}")
        }
        return frame
    }

    /** Returns null only when the stream ends cleanly before the next frame begins. */
    fun readFrom(input: InputStream): WireFrame? {
        val first = input.read()
        if (first < 0) return null

        val prefix = ByteArray(4)
        prefix[0] = first.toByte()
        readFully(input, prefix, 1, 3, "frame length")
        val bodyLength = ((prefix[0].toInt() and 0xff) shl 24) or
            ((prefix[1].toInt() and 0xff) shl 16) or
            ((prefix[2].toInt() and 0xff) shl 8) or
            (prefix[3].toInt() and 0xff)
        if (bodyLength < ENVELOPE_LENGTH || bodyLength > MAX_FRAME_LENGTH) {
            throw WireFormatException("Invalid frame body length: $bodyLength")
        }

        val body = ByteArray(bodyLength)
        readFully(input, body, 0, body.size, "frame body")
        val data = DataInputStream(ByteArrayInputStream(body))
        val magic = data.readUnsignedShort()
        if (magic != MAGIC) throw WireFormatException("Invalid frame magic: 0x${magic.toString(16)}")
        val version = data.readUnsignedShort()
        if (version == 0) throw WireFormatException("Protocol version 0 is invalid")
        val typeId = data.readUnsignedShort()
        if (typeId == 0) throw WireFormatException("Message type 0 is invalid")
        val flags = data.readUnsignedShort()
        val payload = ByteArray(bodyLength - ENVELOPE_LENGTH)
        data.readFully(payload)

        val message = WireMessageCodec.decode(version, typeId, payload)
        return WireFrame(version, message, flags)
    }

    private fun readFully(
        input: InputStream,
        destination: ByteArray,
        offset: Int,
        length: Int,
        description: String,
    ) {
        var position = offset
        val end = offset + length
        while (position < end) {
            val count = input.read(destination, position, end - position)
            if (count < 0) throw EOFException("Truncated $description")
            if (count == 0) {
                val byte = input.read()
                if (byte < 0) throw EOFException("Truncated $description")
                destination[position++] = byte.toByte()
            } else {
                position += count
            }
        }
    }
}

object WireMessageCodec {
    private const val MAX_DEVICE_NAME_BYTES = 255
    private const val MAX_FINGERPRINT_BYTES = 512
    private const val MAX_AUTH_BYTES = 4 * 1024
    private const val MAX_CODEC_CONFIG_BYTES = 1024 * 1024
    private const val MAX_MEDIA_PACKET_BYTES = 15 * 1024 * 1024
    private const val MAX_TEXT_CONTROL_BYTES = 16 * 1024
    private const val MAX_BYE_DETAIL_BYTES = 1024
    private const val MAX_URL_BYTES = 4 * 1024
    private const val MAX_TITLE_BYTES = 512
    private const val MAX_MIME_BYTES = 255

    fun encode(protocolVersion: Int, message: WireMessage): ByteArray {
        if (message is UnknownMessage) return message.payload.toByteArray()
        requireSupported(protocolVersion)
        if (!BrowserWireRules.isAllowedAtVersion(protocolVersion, message)) {
            throw WireFormatException("Browser protocol values require version 2; frame declared version $protocolVersion")
        }
        if (BrowserWireRules.isBrowserMessage(message)) {
            return when {
                message.typeId >= WireMessageType.BROWSER_WORKSPACE_COMMAND.id ->
                    BrowserWirePhase3Codec.encode(message)
                message.typeId >= WireMessageType.BROWSER_TAB_COMMAND.id ->
                    BrowserWirePhase2Codec.encode(message)
                else -> BrowserWireCodec.encode(message)
            }
        }

        val writer = PayloadWriter()
        when (message) {
            is HelloMessage -> {
                writer.u16(message.minimumVersion)
                writer.u16(message.maximumVersion)
                writer.utf8U16(message.deviceName, MAX_DEVICE_NAME_BYTES)
                writer.u16(message.codecCapabilities.size)
                message.codecCapabilities.sortedBy { it.value }.forEach { writer.u16(it.value) }
                writer.i32(message.screenWidth)
                writer.i32(message.screenHeight)
                writer.i32(message.densityDpi)
            }

            is AuthMessage -> {
                writer.u8(message.method.id)
                writer.binary(message.credential, MAX_AUTH_BYTES)
                writer.nullableUtf8U16(message.publicKeyFingerprint, MAX_FINGERPRINT_BYTES)
            }

            is VideoConfigMessage -> {
                writer.u16(message.codec.value)
                writer.i32(message.width)
                writer.i32(message.height)
                writer.u8(message.codecSpecificData.size)
                message.codecSpecificData.forEach { writer.binary(it, MAX_CODEC_CONFIG_BYTES) }
            }

            is VideoPacket -> {
                writer.i64(message.presentationTimeUs)
                writer.boolean(message.keyFrame)
                writer.binary(message.data, MAX_MEDIA_PACKET_BYTES)
            }

            is AudioConfigMessage -> {
                writer.u16(message.codec.value)
                writer.i32(message.sampleRateHz)
                writer.u8(message.channelCount)
                writer.binary(message.codecSpecificData, MAX_CODEC_CONFIG_BYTES)
            }

            is AudioPacket -> {
                writer.i64(message.presentationTimeUs)
                writer.binary(message.data, MAX_MEDIA_PACKET_BYTES)
            }

            is ControlMessage -> encodeControl(writer, message)
            is StatsMessage -> {
                writer.i32(message.receiverQueueDepth)
                writer.i64(message.decodeLatencyUs)
                writer.i64(message.roundTripTimeUs)
                writer.i64(message.droppedVideoFrames)
            }

            is ByeMessage -> {
                writer.u8(message.reason.id)
                writer.utf8U16(message.detail, MAX_BYE_DETAIL_BYTES)
            }

            is MediaCommandMessage -> {
                writer.u8(message.action.id)
                writer.utf8U16(message.url, MAX_URL_BYTES)
                writer.utf8U16(message.title, MAX_TITLE_BYTES)
                writer.utf8U16(message.mimeType, MAX_MIME_BYTES)
                writer.i64(message.durationMs)
                writer.i64(message.startPositionMs)
                writer.nullableUtf8U16(message.subtitleUrl, MAX_URL_BYTES)
            }

            is MediaDataMessage -> {
                writer.u8(if (message.isFinal) 1 else 0)
                writer.binary(message.data, MAX_MEDIA_PACKET_BYTES)
            }

            is SurfaceMessage -> {
                if (message.mode == SurfaceMode.BROWSER && protocolVersion < 2) {
                    throw WireFormatException("Browser surface requires protocol version 2")
                }
                writer.u8(message.mode.id)
                writer.utf8U16(message.caption, MAX_TITLE_BYTES)
            }

            is PlaybackStateMessage -> {
                writer.u8(message.state.id)
                writer.i64(message.positionMs)
                writer.i64(message.durationMs)
                writer.utf8U16(message.detail, MAX_BYE_DETAIL_BYTES)
            }

            is UnknownMessage -> error("Handled above")
            is BrowserCapabilityMessage,
            is BrowserCommandMessage,
            is BrowserInputMessage,
            is BrowserStateMessage,
            is BrowserPreviewMessage,
            is BrowserDialogMessage,
            is BrowserDialogReplyMessage,
            is BrowserTabCommandMessage,
            is BrowserTabStateMessage,
            is BrowserViewCommandMessage,
            is BrowserViewStateMessage,
            is BrowserFaviconMessage,
            is BrowserLibraryCommandMessage,
            is BrowserLibraryStateMessage,
            is BrowserProfileCommandMessage,
            is BrowserProfileStateMessage,
            is BrowserNetworkCommandMessage,
            is BrowserNetworkStateMessage,
            is BrowserWorkspaceCommandMessage,
            is BrowserWorkspaceStateMessage,
            is BrowserWorkspaceResizeMessage,
            is BrowserWorkspaceGeometryMessage,
            is BrowserWorkspaceInputMessage,
            -> error("Handled above")
        }
        return writer.toByteArray()
    }

    fun decode(protocolVersion: Int, typeId: Int, payload: ByteArray): WireMessage {
        val type = WireMessageType.fromId(typeId)
            ?: return UnknownMessage(typeId, BinaryData.of(payload))
        requireSupported(protocolVersion)
        if (BrowserWireRules.isBrowserType(typeId)) {
            if (protocolVersion < 2) {
                throw WireFormatException("Browser message types require protocol version 2")
            }
            if (typeId >= 35 && protocolVersion < 4) throw WireFormatException("Workspace resizing requires protocol version 4")
            return when {
                typeId >= WireMessageType.BROWSER_WORKSPACE_COMMAND.id ->
                    BrowserWirePhase3Codec.decode(type, payload)
                typeId >= WireMessageType.BROWSER_TAB_COMMAND.id ->
                    BrowserWirePhase2Codec.decode(type, payload)
                else -> BrowserWireCodec.decode(type, payload)
            }
        }
        val reader = PayloadReader(payload)
        val message = try {
            when (type) {
                WireMessageType.HELLO -> HelloMessage(
                    minimumVersion = reader.u16("minimum version"),
                    maximumVersion = reader.u16("maximum version"),
                    deviceName = reader.utf8U16(MAX_DEVICE_NAME_BYTES, "device name"),
                    codecCapabilities = buildSet {
                        val count = reader.u16("codec count")
                        if (count > 256) throw WireFormatException("Too many codec capabilities: $count")
                        repeat(count) { add(CodecId(reader.u16("codec id"))) }
                    },
                    screenWidth = reader.i32("screen width"),
                    screenHeight = reader.i32("screen height"),
                    densityDpi = reader.i32("density dpi"),
                )

                WireMessageType.AUTH -> AuthMessage(
                    method = AuthMethod.fromId(reader.u8("auth method"))
                        ?: throw WireFormatException("Unknown auth method"),
                    credential = reader.binary(MAX_AUTH_BYTES, "credential"),
                    publicKeyFingerprint = reader.nullableUtf8U16(
                        MAX_FINGERPRINT_BYTES,
                        "public-key fingerprint",
                    ),
                )

                WireMessageType.VIDEO_CONFIG -> VideoConfigMessage(
                    codec = CodecId(reader.u16("video codec")),
                    width = reader.i32("video width"),
                    height = reader.i32("video height"),
                    codecSpecificData = buildList {
                        val count = reader.u8("video config count")
                        if (count > 16) throw WireFormatException("Too many video config blocks: $count")
                        repeat(count) {
                            add(reader.binary(MAX_CODEC_CONFIG_BYTES, "video config block"))
                        }
                    },
                )

                WireMessageType.VIDEO -> VideoPacket(
                    presentationTimeUs = reader.i64("video presentation time"),
                    keyFrame = reader.boolean("key-frame flag"),
                    data = reader.binary(MAX_MEDIA_PACKET_BYTES, "video packet"),
                )

                WireMessageType.AUDIO_CONFIG -> AudioConfigMessage(
                    codec = CodecId(reader.u16("audio codec")),
                    sampleRateHz = reader.i32("sample rate"),
                    channelCount = reader.u8("channel count"),
                    codecSpecificData = reader.binary(MAX_CODEC_CONFIG_BYTES, "audio config"),
                )

                WireMessageType.AUDIO -> AudioPacket(
                    presentationTimeUs = reader.i64("audio presentation time"),
                    data = reader.binary(MAX_MEDIA_PACKET_BYTES, "audio packet"),
                )

                WireMessageType.CONTROL -> decodeControl(reader)
                WireMessageType.STATS -> StatsMessage(
                    receiverQueueDepth = reader.i32("receiver queue depth"),
                    decodeLatencyUs = reader.i64("decode latency"),
                    roundTripTimeUs = reader.i64("round-trip time"),
                    droppedVideoFrames = reader.i64("dropped video frames"),
                )

                WireMessageType.BYE -> ByeMessage(
                    reason = ByeReason.fromId(reader.u8("bye reason"))
                        ?: throw WireFormatException("Unknown bye reason"),
                    detail = reader.utf8U16(MAX_BYE_DETAIL_BYTES, "bye detail"),
                )

                WireMessageType.MEDIA_COMMAND -> MediaCommandMessage(
                    action = MediaAction.fromId(reader.u8("media action"))
                        ?: throw WireFormatException("Unknown media action"),
                    url = reader.utf8U16(MAX_URL_BYTES, "media url"),
                    title = reader.utf8U16(MAX_TITLE_BYTES, "media title"),
                    mimeType = reader.utf8U16(MAX_MIME_BYTES, "media mime type"),
                    durationMs = reader.i64("media duration"),
                    startPositionMs = reader.i64("media start position"),
                    subtitleUrl = reader.nullableUtf8U16(MAX_URL_BYTES, "subtitle url"),
                )

                WireMessageType.MEDIA_DATA -> MediaDataMessage(
                    isFinal = reader.boolean("media data final flag"),
                    data = reader.binary(MAX_MEDIA_PACKET_BYTES, "media data chunk"),
                )

                WireMessageType.SURFACE -> decodeSurface(protocolVersion, reader)

                WireMessageType.PLAYBACK_STATE -> PlaybackStateMessage(
                    state = PlaybackState.fromId(reader.u8("playback state"))
                        ?: throw WireFormatException("Unknown playback state"),
                    positionMs = reader.i64("playback position"),
                    durationMs = reader.i64("playback duration"),
                    detail = reader.utf8U16(MAX_BYE_DETAIL_BYTES, "playback detail"),
                )

                WireMessageType.BROWSER_CAPABILITY,
                WireMessageType.BROWSER_COMMAND,
                WireMessageType.BROWSER_INPUT,
                WireMessageType.BROWSER_STATE,
                WireMessageType.BROWSER_PREVIEW,
                WireMessageType.BROWSER_DIALOG,
                WireMessageType.BROWSER_DIALOG_REPLY,
                WireMessageType.BROWSER_TAB_COMMAND,
                WireMessageType.BROWSER_TAB_STATE,
                WireMessageType.BROWSER_VIEW_COMMAND,
                WireMessageType.BROWSER_VIEW_STATE,
                WireMessageType.BROWSER_FAVICON,
                WireMessageType.BROWSER_LIBRARY_COMMAND,
                WireMessageType.BROWSER_LIBRARY_STATE,
                WireMessageType.BROWSER_PROFILE_COMMAND,
                WireMessageType.BROWSER_PROFILE_STATE,
                WireMessageType.BROWSER_NETWORK_COMMAND,
                WireMessageType.BROWSER_NETWORK_STATE,
                WireMessageType.BROWSER_WORKSPACE_COMMAND,
                WireMessageType.BROWSER_WORKSPACE_STATE,
                WireMessageType.BROWSER_WORKSPACE_RESIZE,
                WireMessageType.BROWSER_WORKSPACE_GEOMETRY,
                WireMessageType.BROWSER_WORKSPACE_INPUT,
                -> error("Browser message types were handled before the legacy decoder")
            }
        } catch (exception: WireFormatException) {
            throw exception
        } catch (exception: IllegalArgumentException) {
            throw WireFormatException("Invalid ${type.name} payload: ${exception.message}", exception)
        }
        reader.requireFinished()
        return message
    }

    private fun encodeControl(writer: PayloadWriter, message: ControlMessage) {
        writer.i64(message.sequenceNumber)
        writer.u8(message.event.eventId)
        when (val event = message.event) {
            is TransportControl -> {
                writer.u8(event.action.id)
                writer.i64(event.positionMs)
            }

            is PointerControl -> {
                writer.u8(event.action.id)
                writer.f32(event.x)
                writer.f32(event.y)
                writer.i32(event.buttons)
            }

            is KeyControl -> {
                writer.u8(event.action.id)
                writer.i32(event.keyCode)
            }

            is TextControl -> writer.utf8I32(event.text, MAX_TEXT_CONTROL_BYTES)
            is VolumeControl -> writer.f32(event.level)
        }
    }

    private fun decodeControl(reader: PayloadReader): ControlMessage {
        val sequence = reader.i64("control sequence")
        val event = when (val eventId = reader.u8("control event type")) {
            1 -> TransportControl(
                action = TransportAction.fromId(reader.u8("transport action"))
                    ?: throw WireFormatException("Unknown transport action"),
                positionMs = reader.i64("transport position"),
            )

            2 -> PointerControl(
                action = PointerAction.fromId(reader.u8("pointer action"))
                    ?: throw WireFormatException("Unknown pointer action"),
                x = reader.f32("pointer x"),
                y = reader.f32("pointer y"),
                buttons = reader.i32("pointer buttons"),
            )

            3 -> KeyControl(
                action = KeyAction.fromId(reader.u8("key action"))
                    ?: throw WireFormatException("Unknown key action"),
                keyCode = reader.i32("key code"),
            )

            4 -> TextControl(reader.utf8I32(MAX_TEXT_CONTROL_BYTES, "text input"))
            5 -> VolumeControl(reader.f32("volume"))
            else -> throw WireFormatException("Unknown control event type: $eventId")
        }
        return ControlMessage(sequence, event)
    }

    private fun decodeSurface(protocolVersion: Int, reader: PayloadReader): SurfaceMessage {
        val mode = SurfaceMode.fromId(reader.u8("surface mode"))
            ?: throw WireFormatException("Unknown surface mode")
        if (mode == SurfaceMode.BROWSER && protocolVersion < 2) {
            throw WireFormatException("Browser surface requires protocol version 2")
        }
        return SurfaceMessage(mode, reader.utf8U16(MAX_TITLE_BYTES, "surface caption"))
    }

    private fun requireSupported(version: Int) {
        if (version !in ProtocolVersion.MIN_SUPPORTED..ProtocolVersion.CURRENT) {
            throw UnsupportedProtocolVersionException(version)
        }
    }
}

internal class PayloadWriter {
    private val bytes = ByteArrayOutputStream()
    private val data = DataOutputStream(bytes)

    fun u8(value: Int) {
        if (value !in 0..0xff) throw WireFormatException("Value is not u8: $value")
        data.writeByte(value)
    }

    fun u16(value: Int) {
        if (value !in 0..0xffff) throw WireFormatException("Value is not u16: $value")
        data.writeShort(value)
    }

    fun i32(value: Int) = data.writeInt(value)
    fun i64(value: Long) = data.writeLong(value)
    fun f32(value: Float) = data.writeFloat(value)
    fun boolean(value: Boolean) = u8(if (value) 1 else 0)

    fun utf8U16(value: String, maximumBytes: Int) {
        val encoded = value.toByteArray(StandardCharsets.UTF_8)
        if (encoded.size > maximumBytes || encoded.size > 0xffff) {
            throw WireFormatException("UTF-8 field is too long: ${encoded.size} bytes")
        }
        u16(encoded.size)
        data.write(encoded)
    }

    fun nullableUtf8U16(value: String?, maximumBytes: Int) {
        boolean(value != null)
        if (value != null) utf8U16(value, maximumBytes)
    }

    fun utf8I32(value: String, maximumBytes: Int) {
        val encoded = value.toByteArray(StandardCharsets.UTF_8)
        if (encoded.size > maximumBytes) {
            throw WireFormatException("UTF-8 field is too long: ${encoded.size} bytes")
        }
        i32(encoded.size)
        data.write(encoded)
    }

    fun utf8U32(value: String, maximumBytes: Int) {
        val encoded = value.toByteArray(StandardCharsets.UTF_8)
        if (encoded.size > maximumBytes) {
            throw WireFormatException("UTF-8 field is too long: ${encoded.size} bytes")
        }
        u32(encoded.size.toLong())
        data.write(encoded)
    }

    fun u32(value: Long) {
        if (value !in 0..0xffff_ffffL) throw WireFormatException("Value is not u32: $value")
        data.writeInt(value.toInt())
    }

    fun binary(value: BinaryData, maximumBytes: Int) {
        if (value.size > maximumBytes) {
            throw WireFormatException("Binary field is too long: ${value.size} bytes")
        }
        i32(value.size)
        value.withBytes { data.write(it) }
    }

    fun toByteArray(): ByteArray = bytes.toByteArray()
}

internal class PayloadReader(payload: ByteArray) {
    private val bytes = ByteArrayInputStream(payload)
    private val data = DataInputStream(bytes)

    fun u8(description: String): Int = guarded(description) { data.readUnsignedByte() }
    fun u16(description: String): Int = guarded(description) { data.readUnsignedShort() }
    fun i32(description: String): Int = guarded(description) { data.readInt() }
    fun i64(description: String): Long = guarded(description) { data.readLong() }
    fun f32(description: String): Float = guarded(description) { data.readFloat() }

    fun boolean(description: String): Boolean = when (val value = u8(description)) {
        0 -> false
        1 -> true
        else -> throw WireFormatException("Invalid $description: $value")
    }

    fun utf8U16(maximumBytes: Int, description: String): String {
        val length = u16("$description length")
        return utf8(length, maximumBytes, description)
    }

    fun nullableUtf8U16(maximumBytes: Int, description: String): String? =
        if (boolean("$description presence")) utf8U16(maximumBytes, description) else null

    fun utf8I32(maximumBytes: Int, description: String): String {
        val length = i32("$description length")
        return utf8(length, maximumBytes, description)
    }

    fun utf8U32(maximumBytes: Int, description: String): String {
        val length = u32("$description length")
        if (length > Int.MAX_VALUE.toLong()) {
            throw WireFormatException("Invalid $description length: $length")
        }
        return utf8(length.toInt(), maximumBytes, description)
    }

    fun u32(description: String): Long = guarded(description) {
        data.readInt().toLong() and 0xffff_ffffL
    }

    fun binary(maximumBytes: Int, description: String): BinaryData {
        val length = i32("$description length")
        if (length < 0 || length > maximumBytes) {
            throw WireFormatException("Invalid $description length: $length")
        }
        if (length > bytes.available()) throw WireFormatException("Truncated $description")
        val value = ByteArray(length)
        guarded(description) { data.readFully(value) }
        return BinaryData.of(value)
    }

    fun requireFinished() {
        if (bytes.available() != 0) {
            throw WireFormatException("Trailing payload bytes: ${bytes.available()}")
        }
    }

    private fun utf8(length: Int, maximumBytes: Int, description: String): String {
        if (length < 0 || length > maximumBytes) {
            throw WireFormatException("Invalid $description length: $length")
        }
        if (length > bytes.available()) throw WireFormatException("Truncated $description")
        val encoded = ByteArray(length)
        guarded(description) { data.readFully(encoded) }
        return try {
            StandardCharsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(encoded))
                .toString()
        } catch (exception: CharacterCodingException) {
            throw WireFormatException("Invalid UTF-8 in $description", exception)
        }
    }

    private inline fun <T> guarded(description: String, block: () -> T): T = try {
        block()
    } catch (exception: EOFException) {
        throw WireFormatException("Truncated $description", exception)
    }
}

