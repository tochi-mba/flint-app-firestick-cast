package com.rextechnologies.flint.castcore.copy

/** The Media tab: pick a file, push it, drive the transport. */
object MediaCopy {
    val empty: EmptyStateCopy = EmptyStateCopy(
        glyph = "M",
        title = "Nothing picked yet",
        body = "Choose a video and Flint hands it to the television, which plays it at its original " +
            "quality. Nothing is re-encoded on the way.",
    )

    const val PICK_ACTION: String = "Choose a video"
    const val STOP_ACTION: String = "Stop playback"

    fun pushing(chunksSent: Long, chunksTotal: Long): String {
        require(chunksTotal > 0)
        val percent = ((chunksSent.coerceIn(0, chunksTotal) * 100) / chunksTotal)
        return "Sending to the TV — $percent%."
    }

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
}
