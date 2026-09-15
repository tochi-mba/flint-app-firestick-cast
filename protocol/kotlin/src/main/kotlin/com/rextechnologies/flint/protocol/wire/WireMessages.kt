package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData

object ProtocolVersion {
    const val MIN_SUPPORTED: Int = 1
    const val CURRENT: Int = 4
}

enum class WireMessageType(val id: Int) {
    HELLO(1),
    AUTH(2),
    VIDEO_CONFIG(3),
    VIDEO(4),
    AUDIO_CONFIG(5),
    AUDIO(6),
    CONTROL(7),
    STATS(8),
    BYE(9),
    MEDIA_COMMAND(10),
    SURFACE(11),
    PLAYBACK_STATE(12),
    MEDIA_DATA(13),
    BROWSER_CAPABILITY(14),
    BROWSER_COMMAND(15),
    BROWSER_INPUT(16),
    BROWSER_STATE(17),
    BROWSER_PREVIEW(18),
    BROWSER_DIALOG(19),
    BROWSER_DIALOG_REPLY(20),
    BROWSER_TAB_COMMAND(21),
    BROWSER_TAB_STATE(22),
    BROWSER_VIEW_COMMAND(23),
    BROWSER_VIEW_STATE(24),
    BROWSER_FAVICON(25),
    BROWSER_LIBRARY_COMMAND(26),
    BROWSER_LIBRARY_STATE(27),
    BROWSER_PROFILE_COMMAND(28),
    BROWSER_PROFILE_STATE(29),
    BROWSER_NETWORK_COMMAND(30),
    BROWSER_NETWORK_STATE(31),
    BROWSER_WORKSPACE_COMMAND(32),
    BROWSER_WORKSPACE_STATE(33),
    BROWSER_WORKSPACE_INPUT(34),
    BROWSER_WORKSPACE_RESIZE(35),
    BROWSER_WORKSPACE_GEOMETRY(36),
    ;

    companion object {
        fun fromId(id: Int): WireMessageType? = entries.firstOrNull { it.id == id }
    }
}

/** Numeric media codec identifier. Unknown positive values remain representable. */
@JvmInline
value class CodecId(val value: Int) {
    init {
        require(value in 1..0xffff) { "Codec id must be an unsigned 16-bit non-zero value" }
    }

    companion object {
        val H264 = CodecId(1)
        val H265 = CodecId(2)
        val AAC_LC = CodecId(3)
        val OPUS = CodecId(4)
        val AV1 = CodecId(5)
    }
}

sealed interface WireMessage {
    val typeId: Int
}

data class HelloMessage(
    val minimumVersion: Int,
    val maximumVersion: Int,
    val deviceName: String,
    val codecCapabilities: Set<CodecId>,
    val screenWidth: Int,
    val screenHeight: Int,
    val densityDpi: Int,
) : WireMessage {
    init {
        require(minimumVersion in 1..0xffff)
        require(maximumVersion in minimumVersion..0xffff)
        require(deviceName.isNotBlank())
        require(screenWidth > 0 && screenHeight > 0)
        require(densityDpi > 0)
    }

    override val typeId: Int = WireMessageType.HELLO.id
}

enum class AuthMethod(val id: Int) {
    PAIRING_CODE(1),
    SESSION_TOKEN(2),
    PUBLIC_KEY_PROOF(3),
    ;

    companion object {
        fun fromId(id: Int): AuthMethod? = entries.firstOrNull { it.id == id }
    }
}

data class AuthMessage(
    val method: AuthMethod,
    val credential: BinaryData,
    val publicKeyFingerprint: String? = null,
) : WireMessage {
    init {
        require(!credential.isEmpty) { "Credential must not be empty" }
        require(publicKeyFingerprint == null || publicKeyFingerprint.isNotBlank())
    }

    override val typeId: Int = WireMessageType.AUTH.id
}

data class VideoConfigMessage(
    val codec: CodecId,
    val width: Int,
    val height: Int,
    val codecSpecificData: List<BinaryData>,
) : WireMessage {
    init {
        require(width > 0 && height > 0)
        require(codecSpecificData.size <= 16)
    }

    override val typeId: Int = WireMessageType.VIDEO_CONFIG.id
}

data class VideoPacket(
    val presentationTimeUs: Long,
    val keyFrame: Boolean,
    val data: BinaryData,
) : WireMessage {
    init {
        require(presentationTimeUs >= 0)
        require(!data.isEmpty)
    }

    override val typeId: Int = WireMessageType.VIDEO.id
}

data class AudioConfigMessage(
    val codec: CodecId,
    val sampleRateHz: Int,
    val channelCount: Int,
    val codecSpecificData: BinaryData = BinaryData.EMPTY,
) : WireMessage {
    init {
        require(sampleRateHz in 1..768_000)
        require(channelCount in 1..32)
    }

    override val typeId: Int = WireMessageType.AUDIO_CONFIG.id
}

data class AudioPacket(
    val presentationTimeUs: Long,
    val data: BinaryData,
) : WireMessage {
    init {
        require(presentationTimeUs >= 0)
        require(!data.isEmpty)
    }

    override val typeId: Int = WireMessageType.AUDIO.id
}

enum class TransportAction(val id: Int) {
    PLAY(1),
    PAUSE(2),
    STOP(3),
    SEEK_TO(4),
    NEXT(5),
    PREVIOUS(6),
    ;

    companion object {
        fun fromId(id: Int): TransportAction? = entries.firstOrNull { it.id == id }
    }
}

enum class PointerAction(val id: Int) {
    MOVE(1),
    DOWN(2),
    UP(3),
    SCROLL(4),
    ;

    companion object {
        fun fromId(id: Int): PointerAction? = entries.firstOrNull { it.id == id }
    }
}

enum class KeyAction(val id: Int) {
    DOWN(1),
    UP(2),
    ;

    companion object {
        fun fromId(id: Int): KeyAction? = entries.firstOrNull { it.id == id }
    }
}

sealed interface ControlEvent {
    val eventId: Int
}

data class TransportControl(
    val action: TransportAction,
    /** Used by [TransportAction.SEEK_TO]; -1 for actions without a position. */
    val positionMs: Long = -1,
) : ControlEvent {
    init {
        require(positionMs >= -1)
        require(action == TransportAction.SEEK_TO || positionMs == -1L)
        require(action != TransportAction.SEEK_TO || positionMs >= 0)
    }

    override val eventId: Int = 1
}

data class PointerControl(
    val action: PointerAction,
    val x: Float,
    val y: Float,
    val buttons: Int = 0,
) : ControlEvent {
    init {
        require(x.isFinite() && y.isFinite())
    }

    override val eventId: Int = 2
}

data class KeyControl(
    val action: KeyAction,
    val keyCode: Int,
) : ControlEvent {
    init {
        require(keyCode >= 0)
    }

    override val eventId: Int = 3
}

data class TextControl(val text: String) : ControlEvent {
    override val eventId: Int = 4
}

data class VolumeControl(val level: Float) : ControlEvent {
    init {
        require(level.isFinite() && level in 0.0f..1.0f)
    }

    override val eventId: Int = 5
}

data class ControlMessage(
    val sequenceNumber: Long,
    val event: ControlEvent,
) : WireMessage {
    init {
        require(sequenceNumber >= 0)
    }

    override val typeId: Int = WireMessageType.CONTROL.id
}

data class StatsMessage(
    val receiverQueueDepth: Int,
    val decodeLatencyUs: Long,
    val roundTripTimeUs: Long,
    val droppedVideoFrames: Long,
) : WireMessage {
    init {
        require(receiverQueueDepth >= 0)
        require(decodeLatencyUs >= 0)
        require(roundTripTimeUs >= 0)
        require(droppedVideoFrames >= 0)
    }

    override val typeId: Int = WireMessageType.STATS.id
}

enum class ByeReason(val id: Int) {
    NORMAL(1),
    AUTHENTICATION_FAILED(2),
    UNSUPPORTED_VERSION(3),
    PROTOCOL_ERROR(4),
    RECEIVER_STOPPED(5),
    ;

    companion object {
        fun fromId(id: Int): ByeReason? = entries.firstOrNull { it.id == id }
    }
}

data class ByeMessage(
    val reason: ByeReason,
    val detail: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.BYE.id
}

enum class MediaAction(val id: Int) {
    /** Replace whatever the receiver is showing with this item. */
    LOAD(1),

    /** Return the receiver to its idle screen and release the decoder. */
    CLEAR(2),
    ;

    companion object {
        fun fromId(id: Int): MediaAction? = entries.firstOrNull { it.id == id }
    }
}

/**
 * Tells the receiver which item to fetch and play.
 *
 * The phone usually sends a URL rather than the bytes themselves: for
 * already-encoded media the receiver pulls straight from the phone HTTP
 * server, or from the internet over the same hotspot, so nothing is
 * transcoded on the way. When [url] is blank, the receiver instead plays the
 * file most recently pushed to it in full over this same connection via
 * [MediaDataMessage] — the fallback for a receiver whose platform silently
 * drops outbound connections it initiates to a private LAN address (observed
 * on some Fire OS builds), even though the same address works perfectly for
 * this connection, which the phone initiated.
 */
data class MediaCommandMessage(
    val action: MediaAction,
    val url: String = "",
    val title: String = "",
    val mimeType: String = "",
    val durationMs: Long = -1,
    val startPositionMs: Long = 0,
    val subtitleUrl: String? = null,
) : WireMessage {
    init {
        require(durationMs >= -1) { "Duration must be -1 when unknown" }
        require(startPositionMs >= 0) { "Start position must not be negative" }
        if (action == MediaAction.LOAD) {
            require(mimeType.isNotBlank()) { "LOAD requires a MIME type" }
        }
        require(subtitleUrl == null || subtitleUrl.isNotBlank())
    }

    override val typeId: Int = WireMessageType.MEDIA_COMMAND.id
}

/** One chunk of a media file pushed to the receiver over the control connection, in order. */
data class MediaDataMessage(
    val data: BinaryData,
    val isFinal: Boolean,
) : WireMessage {
    override val typeId: Int = WireMessageType.MEDIA_DATA.id
}

enum class SurfaceMode(val id: Int) {
    IDLE(1),
    PLAYER(2),
    MIRROR(3),
    PRESENTATION(4),

    /** Valid only in v2 and only on the TLS BrowserSession. */
    BROWSER(5),
    ;

    companion object {
        fun fromId(id: Int): SurfaceMode? = entries.firstOrNull { it.id == id }
    }
}

/** Switches what the receiver renders. [caption] is shown on the idle screen. */
data class SurfaceMessage(
    val mode: SurfaceMode,
    val caption: String = "",
) : WireMessage {
    override val typeId: Int = WireMessageType.SURFACE.id
}

enum class PlaybackState(val id: Int) {
    IDLE(1),
    BUFFERING(2),
    PLAYING(3),
    PAUSED(4),
    ENDED(5),
    ERROR(6),
    ;

    companion object {
        fun fromId(id: Int): PlaybackState? = entries.firstOrNull { it.id == id }
    }
}

/** Receiver-to-phone playback report, which drives the remote and the scrubber. */
data class PlaybackStateMessage(
    val state: PlaybackState,
    val positionMs: Long = 0,
    val durationMs: Long = -1,
    val detail: String = "",
) : WireMessage {
    init {
        require(positionMs >= 0)
        require(durationMs >= -1)
    }

    override val typeId: Int = WireMessageType.PLAYBACK_STATE.id
}

/** A future message type that this build does not understand but can relay. */
data class UnknownMessage(
    override val typeId: Int,
    val payload: BinaryData,
) : WireMessage {
    init {
        require(typeId in 1..0xffff)
        require(WireMessageType.fromId(typeId) == null) {
            "Known message type $typeId must use its typed model"
        }
    }
}

data class WireFrame(
    val protocolVersion: Int,
    val message: WireMessage,
    val flags: Int = 0,
) {
    init {
        require(protocolVersion in 1..0xffff)
        require(flags in 0..0xffff)
    }
}

object VersionNegotiator {
    fun negotiate(
        localMinimum: Int = ProtocolVersion.MIN_SUPPORTED,
        localMaximum: Int = ProtocolVersion.CURRENT,
        remote: HelloMessage,
    ): Int? {
        require(localMinimum in 1..localMaximum)
        val lower = maxOf(localMinimum, remote.minimumVersion)
        val upper = minOf(localMaximum, remote.maximumVersion)
        return if (lower <= upper) upper else null
    }
}
