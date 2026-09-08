package com.rextechnologies.flint.receiver

/** Converts Media3's implementation-facing error identifiers into useful television copy. */
internal fun friendlyPlaybackError(errorCodeName: String): String = when (errorCodeName) {
    "ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT",
    "ERROR_CODE_IO_NETWORK_CONNECTION_FAILED",
    -> "This TV couldn't reach the media on your PC. Keep Flint open and make sure both devices stay on the same network."

    "ERROR_CODE_IO_BAD_HTTP_STATUS" ->
        "The media source rejected the request. Choose the item again in Flint."

    "ERROR_CODE_DECODING_FAILED",
    "ERROR_CODE_DECODING_FORMAT_EXCEEDS_CAPABILITIES",
    "ERROR_CODE_DECODING_FORMAT_UNSUPPORTED",
    -> "This TV can't decode that media format. Try an H.264 video with AAC audio."

    "ERROR_CODE_PARSING_CONTAINER_MALFORMED",
    "ERROR_CODE_PARSING_MANIFEST_MALFORMED",
    -> "That media file appears to be damaged or incomplete."

    else -> "Playback stopped unexpectedly. Try the item again from Flint."
}

internal const val RECEIVER_START_FAILURE_MESSAGE =
    "Flint couldn't open the receiver on this network. Try again in a moment."

internal const val MIRROR_FAILURE_MESSAGE =
    "Screen mirroring stopped. Start it again from your PC."
