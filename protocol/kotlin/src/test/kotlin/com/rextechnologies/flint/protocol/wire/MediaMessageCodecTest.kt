package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNull

class MediaMessageCodecTest {
    private fun roundTrip(message: WireMessage): WireMessage =
        WireCodec.decode(WireCodec.encode(WireFrame(ProtocolVersion.CURRENT, message))).message

    @Test
    fun `a load command round-trips every field`() {
        val message = MediaCommandMessage(
            action = MediaAction.LOAD,
            url = "http://192.168.201.1:47856/media/v1/token/item",
            title = "Holiday clip",
            mimeType = "video/mp4",
            durationMs = 3_600_000,
            startPositionMs = 42_000,
            subtitleUrl = "http://192.168.201.1:47856/media/v1/token/subs",
        )

        assertEquals(message, roundTrip(message))
    }

    @Test
    fun `a clear command needs no media fields`() {
        val message = MediaCommandMessage(MediaAction.CLEAR)

        val decoded = roundTrip(message) as MediaCommandMessage
        assertEquals(MediaAction.CLEAR, decoded.action)
        assertEquals(-1, decoded.durationMs)
        assertNull(decoded.subtitleUrl)
    }

    @Test
    fun `a load command without a MIME type is refused`() {
        assertFailsWith<IllegalArgumentException> {
            MediaCommandMessage(MediaAction.LOAD, url = "http://h/x")
        }
        assertFailsWith<IllegalArgumentException> {
            MediaCommandMessage(MediaAction.CLEAR, durationMs = -7)
        }
        assertFailsWith<IllegalArgumentException> {
            MediaCommandMessage(MediaAction.CLEAR, startPositionMs = -1)
        }
        assertFailsWith<IllegalArgumentException> {
            MediaCommandMessage(MediaAction.CLEAR, subtitleUrl = " ")
        }
    }

    @Test
    fun `a load command with no URL is accepted and means play what was just pushed`() {
        // Some Fire OS builds silently drop an outbound connection this app initiates to a private
        // LAN address, so the host may push the file's bytes over this connection instead (see
        // MediaDataMessage) and follow with a LOAD naming no URL at all.
        val message = MediaCommandMessage(MediaAction.LOAD, mimeType = "video/mp4")

        val decoded = roundTrip(message) as MediaCommandMessage
        assertEquals("", decoded.url)
        assertEquals("video/mp4", decoded.mimeType)
    }

    @Test
    fun `a unicode title survives the round trip`() {
        val message = MediaCommandMessage(
            action = MediaAction.LOAD,
            url = "http://h/x",
            title = "Ete 2026 - cafe",
            mimeType = "video/mp4",
        )

        assertEquals("Ete 2026 - cafe", (roundTrip(message) as MediaCommandMessage).title)
    }

    @Test
    fun `surface switches round-trip`() {
        SurfaceMode.entries.forEach { mode ->
            assertEquals(SurfaceMessage(mode, "on hold"), roundTrip(SurfaceMessage(mode, "on hold")))
        }
    }

    @Test
    fun `playback reports round-trip`() {
        PlaybackState.entries.forEach { state ->
            val message = PlaybackStateMessage(state, 1_234, 9_999, "detail")
            assertEquals(message, roundTrip(message))
        }
        assertEquals(-1, roundTrip(PlaybackStateMessage(PlaybackState.IDLE)).let {
            (it as PlaybackStateMessage).durationMs
        })
    }

    @Test
    fun `an invalid playback report is refused`() {
        assertFailsWith<IllegalArgumentException> { PlaybackStateMessage(PlaybackState.IDLE, -1) }
        assertFailsWith<IllegalArgumentException> {
            PlaybackStateMessage(PlaybackState.IDLE, 0, -5)
        }
    }

    @Test
    fun `unknown enum ids are rejected as format errors`() {
        val encoded = WireCodec.encode(
            WireFrame(ProtocolVersion.CURRENT, MediaCommandMessage(MediaAction.CLEAR)),
        )
        // The action byte is the first payload byte after the 4-byte length and 8-byte envelope.
        encoded[12] = 99

        assertFailsWith<WireFormatException> { WireCodec.decode(encoded) }
    }

    @Test
    fun `unknown surface and playback ids are rejected`() {
        val surface = WireCodec.encode(
            WireFrame(ProtocolVersion.CURRENT, SurfaceMessage(SurfaceMode.PLAYER)),
        )
        surface[12] = 88
        assertFailsWith<WireFormatException> { WireCodec.decode(surface) }

        val playback = WireCodec.encode(
            WireFrame(ProtocolVersion.CURRENT, PlaybackStateMessage(PlaybackState.PLAYING)),
        )
        playback[12] = 77
        assertFailsWith<WireFormatException> { WireCodec.decode(playback) }
    }

    @Test
    fun `enum identifiers are stable across builds`() {
        assertEquals(10, WireMessageType.MEDIA_COMMAND.id)
        assertEquals(11, WireMessageType.SURFACE.id)
        assertEquals(12, WireMessageType.PLAYBACK_STATE.id)
        assertEquals(13, WireMessageType.MEDIA_DATA.id)
        assertEquals(MediaAction.LOAD, MediaAction.fromId(1))
        assertNull(MediaAction.fromId(9))
        assertEquals(SurfaceMode.MIRROR, SurfaceMode.fromId(3))
        assertNull(SurfaceMode.fromId(9))
        assertEquals(PlaybackState.ENDED, PlaybackState.fromId(5))
        assertNull(PlaybackState.fromId(9))
    }

    @Test
    fun `a pushed media chunk round-trips its bytes and final flag`() {
        val message = MediaDataMessage(BinaryData.of(byteArrayOf(1, 2, 3, 4, 5)), isFinal = true)

        assertEquals(message, roundTrip(message))
    }

    @Test
    fun `a non-final pushed chunk round-trips too`() {
        val message = MediaDataMessage(BinaryData.of(byteArrayOf(9, 8, 7)), isFinal = false)

        val decoded = roundTrip(message) as MediaDataMessage
        assertEquals(false, decoded.isFinal)
    }

    @Test
    fun `an empty final chunk is valid`() {
        val message = MediaDataMessage(BinaryData.of(byteArrayOf()), isFinal = true)

        assertEquals(message, roundTrip(message))
    }
}

