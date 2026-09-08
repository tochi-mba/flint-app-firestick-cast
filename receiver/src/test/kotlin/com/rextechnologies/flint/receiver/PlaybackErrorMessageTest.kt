package com.rextechnologies.flint.receiver

import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertFalse

class PlaybackErrorMessageTest {
    @Test
    fun `network failures explain the two devices without guessing at one firewall cause`() {
        listOf(
            "ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT",
            "ERROR_CODE_IO_NETWORK_CONNECTION_FAILED",
        ).forEach { code ->
            val message = friendlyPlaybackError(code)
            assertContains(message, "same network")
            assertContains(message, "Keep Flint open")
            assertFalse(message.contains("Firewall"))
        }
    }

    @Test
    fun `bad status asks the user to choose the item again`() {
        assertEquals(
            "The media source rejected the request. Choose the item again in Flint.",
            friendlyPlaybackError("ERROR_CODE_IO_BAD_HTTP_STATUS"),
        )
    }

    @Test
    fun `decoder failures recommend a concrete compatible format`() {
        listOf(
            "ERROR_CODE_DECODING_FAILED",
            "ERROR_CODE_DECODING_FORMAT_EXCEEDS_CAPABILITIES",
            "ERROR_CODE_DECODING_FORMAT_UNSUPPORTED",
        ).forEach { code ->
            val message = friendlyPlaybackError(code)
            assertContains(message, "H.264")
            assertContains(message, "AAC")
        }
    }

    @Test
    fun `malformed media reports damage rather than implementation jargon`() {
        listOf(
            "ERROR_CODE_PARSING_CONTAINER_MALFORMED",
            "ERROR_CODE_PARSING_MANIFEST_MALFORMED",
        ).forEach { code -> assertContains(friendlyPlaybackError(code), "damaged") }
    }

    @Test
    fun `unknown errors remain useful and never expose Media3 identifiers`() {
        val message = friendlyPlaybackError("ERROR_CODE_FUTURE_FAILURE")
        assertEquals("Playback stopped unexpectedly. Try the item again from Flint.", message)
        assertFalse(message.contains("ERROR_CODE"))
        assertFalse(message.contains('_'))
    }

    @Test
    fun `receiver copy does not make an encryption claim`() {
        val allMessages = listOf(
            friendlyPlaybackError("ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT"),
            friendlyPlaybackError("ERROR_CODE_IO_BAD_HTTP_STATUS"),
            friendlyPlaybackError("ERROR_CODE_DECODING_FAILED"),
            friendlyPlaybackError("ERROR_CODE_PARSING_CONTAINER_MALFORMED"),
            friendlyPlaybackError("UNKNOWN"),
            RECEIVER_START_FAILURE_MESSAGE,
            MIRROR_FAILURE_MESSAGE,
        ).joinToString(" ").lowercase()
        listOf("encrypted", "private traffic", "secure connection")
            .forEach { assertFalse(allMessages.contains(it)) }
    }

    @Test
    fun `receiver and mirror failures never expose internal exception text`() {
        assertFalse(RECEIVER_START_FAILURE_MESSAGE.contains("Exception"))
        assertFalse(RECEIVER_START_FAILURE_MESSAGE.contains("EADDR"))
        assertFalse(MIRROR_FAILURE_MESSAGE.contains("MediaCodec"))
        assertFalse(MIRROR_FAILURE_MESSAGE.contains("decoder", ignoreCase = true))
    }
}
