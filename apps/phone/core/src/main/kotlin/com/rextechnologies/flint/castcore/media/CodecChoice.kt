package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.protocol.wire.CodecId

/**
 * Which video codec a session encodes with, decided rather than left to a set's iteration order.
 *
 * The handshake negotiates HEVC first, because it costs a third less bitrate on a shared hotspot.
 * The phone does not follow that preference, for two reasons that are both about the encoder rather
 * than the link. Below API 29 the only lever against B-frames is the profile, and HEVC has no
 * profile that forbids them; and every Fire TV decodes H.264 in hardware while HEVC support varies
 * by generation. So H.264 is first, HEVC is the fallback, and the choice is bounded by what the
 * receiver said it decodes and what this phone's probe found it can encode. A measured reason to
 * prefer HEVC would be a change to this one list.
 */
object CodecChoice {
    val PREFERENCE: List<CodecId> = listOf(CodecId.H264, CodecId.H265)

    /**
     * The codecs to try, in order, for a session with a receiver that decodes [receiverCodecs] on a
     * phone whose probe found [hardwareEncoders]. Empty when the two share nothing.
     */
    fun order(receiverCodecs: Set<CodecId>, hardwareEncoders: Set<CodecId>): List<CodecId> =
        PREFERENCE.filter { it in receiverCodecs && it in hardwareEncoders }

    /** The codec a check exercises before any receiver is known: the one a session would try first. */
    fun preferred(hardwareEncoders: Set<CodecId>): CodecId? =
        PREFERENCE.firstOrNull { it in hardwareEncoders }
}

/** The name a person sees for a codec. One place, so the About card and the notice agree. */
object CodecNames {
    fun label(codec: CodecId): String = when (codec) {
        CodecId.H264 -> "H.264"
        CodecId.H265 -> "H.265"
        CodecId.AAC_LC -> "AAC-LC"
        CodecId.OPUS -> "Opus"
        CodecId.AV1 -> "AV1"
        else -> "Codec ${codec.value}"
    }
}
