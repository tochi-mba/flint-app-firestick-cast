package com.rextechnologies.flint.castcore.copy

import com.rextechnologies.flint.protocol.text.Decimal

/** The Media tab: pick a file, push it, drive the transport. */
object MediaCopy {
    val empty: EmptyStateCopy = EmptyStateCopy(
        glyph = "M",
        title = "Nothing picked yet",
        body = "Choose a video and Flint hands it to the television, which plays it at its original " +
            "quality. Nothing is re-encoded on the way.",
    )

    const val PICK_ACTION: String = "Choose a video"
    const val PICK_ANOTHER_ACTION: String = "Choose another"
    const val PLAY_ACTION: String = "Play"
    const val PAUSE_ACTION: String = "Pause"
    const val STOP_ACTION: String = "Stop playback"
    const val CLEAR_ACTION: String = "Clear the TV"
    const val CANCEL_ACTION: String = "Cancel"
    const val SECTION_NOW: String = "On the television"

    /** The one-word state for the pill. */
    const val PREPARING_WORD: String = "Reading"
    const val SENDING_WORD: String = "Sending"
    const val BUFFERING_WORD: String = "Buffering"
    const val PLAYING_WORD: String = "Playing"
    const val PAUSED_WORD: String = "Paused"
    const val ENDED_WORD: String = "Finished"
    const val FAILED_WORD: String = "Stopped"

    fun pushing(chunksSent: Long, chunksTotal: Long): String {
        require(chunksTotal > 0)
        val percent = ((chunksSent.coerceIn(0, chunksTotal) * 100) / chunksTotal)
        return "Sending to the TV — $percent%."
    }

    /** Progress as bytes, for a provider that would not say how long the file is. */
    fun sendingBytes(bytesSent: Long): String =
        "Sending to the TV — ${Decimal.oneDecimal(bytesSent / BYTES_PER_MEBIBYTE)} MiB so far."

    /** Progress as a fraction, for a provider that did. */
    fun sendingFraction(fraction: Double): String =
        "Sending to the TV — ${(fraction.coerceIn(0.0, 1.0) * 100).toInt()}%."

    /**
     * Why the file is pushed rather than fetched.
     *
     * Shown once, in the advisory block, because the alternative is somebody wondering why a local
     * file takes any time at all to start.
     */
    const val WHY_PUSHED: String =
        "Flint sends the file to the television over the connection this phone opened. Some Fire TV " +
            "builds quietly drop connections the television itself starts to a phone, so sending it " +
            "this way is the route that works rather than the fastest one."

    const val NOT_CONNECTED: String =
        "This phone is not connected to a television. Pair with one on the Cast tab, then choose a " +
            "video here."

    const val UNREADABLE: String =
        "This phone could not read the file it was handed. Choose it again, or choose another."

    const val EMPTY_FILE: String = "That file is empty, so there is nothing to send."

    const val MIME_TOO_LONG: String =
        "The file's type name is too long to send to the television, so Flint cannot say what it " +
            "is. Choose another file."

    const val LINK_LOST: String =
        "The connection to the television stopped taking the file, so the send was abandoned. Check " +
            "that the television is still awake and on this phone's hotspot, then choose it again."

    const val CANCELLED: String = "The send was cancelled and the television was told to forget the part it had."

    /** What the television said when it could not play the file and would not say why. */
    const val RECEIVER_ERROR: String = "The television could not play the file and did not say why."

    /** What happened to a second screen when playback started. */
    const val REPLACED_SECOND_SCREEN: String =
        "The second screen was stopped so the television could play the file. Start it again from " +
            "the Screen tab when playback is over."

    const val REPLACED_MIRROR: String =
        "The mirror was stopped so the television could play the file. Start it again from the " +
            "Screen tab when playback is over."

    /** What did not happen when playback was cleared, said rather than done silently. */
    const val SECOND_SCREEN_NOT_RESTARTED: String =
        "Playback has been cleared. The second screen it replaced was not restarted; start it again " +
            "from the Screen tab if you want it back."

    const val MIRROR_NOT_RESTARTED: String =
        "Playback has been cleared. The mirror it replaced was not restarted; start it again from " +
            "the Screen tab if you want it back."

    /** The size a person sees beside the title, or nothing when the provider would not say. */
    fun sizeLabel(sizeBytes: Long?): String? =
        sizeBytes?.let { "${Decimal.oneDecimal(it / BYTES_PER_MEBIBYTE)} MiB" }

    private const val BYTES_PER_MEBIBYTE = 1024.0 * 1024.0
}
