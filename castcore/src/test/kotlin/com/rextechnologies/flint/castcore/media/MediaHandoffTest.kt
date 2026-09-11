package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.protocol.wire.MediaAction
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class MediaHandoffTest {
    @Test
    fun `the mime cap is the smaller of the three implementations`() {
        // Kotlin's codec rejects anything longer; C# and Rust allow 512. A MIME type between the two
        // encodes on Windows and is refused here, so the smaller value is the only one safe in both
        // directions until the three are reconciled.
        assertEquals(255, MediaHandoff.MAXIMUM_MIME_BYTES)
    }

    @Test
    fun `a file becomes whole chunks, with the last one short`() {
        assertEquals(0, MediaHandoff.chunkCount(0))
        assertEquals(1, MediaHandoff.chunkCount(1))
        assertEquals(1, MediaHandoff.chunkCount(MediaHandoff.CHUNK_BYTES.toLong()))
        assertEquals(2, MediaHandoff.chunkCount(MediaHandoff.CHUNK_BYTES + 1L))
        assertEquals(4, MediaHandoff.chunkCount(MediaHandoff.CHUNK_BYTES * 4L))
        assertFailsWith<IllegalArgumentException> { MediaHandoff.chunkCount(-1) }
    }

    @Test
    fun `an ordinary mime type survives, trimmed`() {
        assertEquals("video/mp4", MediaHandoff.mimeTypeOrNull("  video/mp4 "))
    }

    @Test
    fun `a mime type that will not fit is refused rather than truncated`() {
        // A truncated MIME type is a different MIME type, and the television would choose a decoder
        // for something the file is not.
        assertNull(MediaHandoff.mimeTypeOrNull(""))
        assertNull(MediaHandoff.mimeTypeOrNull("   "))
        assertNull(MediaHandoff.mimeTypeOrNull("video/" + "x".repeat(300)))
        assertNull(MediaHandoff.mimeTypeOrNull("video/mp4\u0000"))
    }

    @Test
    fun `the cap counts bytes rather than characters`() {
        // 128 two-byte characters is 256 bytes, one over the cap, but only 128 characters long.
        val multiByte = "é".repeat(128)
        assertEquals(128, multiByte.length)
        assertNull(MediaHandoff.mimeTypeOrNull(multiByte))
        assertNotNull(MediaHandoff.mimeTypeOrNull("é".repeat(127)))
    }

    @Test
    fun `a title never carries where the file lives`() {
        assertEquals("holiday.mp4", MediaHandoff.titleFor("/storage/emulated/0/Movies/holiday.mp4"))
        assertEquals("holiday.mp4", MediaHandoff.titleFor("C:\\Users\\someone\\holiday.mp4"))
        assertEquals("holiday.mp4", MediaHandoff.titleFor("holiday.mp4"))
    }

    @Test
    fun `a title with nothing in it falls back rather than going out blank`() {
        assertEquals(MediaHandoff.FALLBACK_TITLE, MediaHandoff.titleFor(""))
        assertEquals(MediaHandoff.FALLBACK_TITLE, MediaHandoff.titleFor("/tmp/"))
        assertEquals(MediaHandoff.FALLBACK_TITLE, MediaHandoff.titleFor("\u0000\u0001"))
    }

    @Test
    fun `a very long title is cut to fit the wire`() {
        val trimmed = MediaHandoff.titleFor("n".repeat(2_000))
        assertEquals(MediaHandoff.MAXIMUM_TITLE_BYTES, trimmed.toByteArray(Charsets.UTF_8).size)
    }

    @Test
    fun `a long multi-byte title is cut without leaving a partial character`() {
        val trimmed = MediaHandoff.titleFor("é".repeat(1_000))
        assertTrue(trimmed.toByteArray(Charsets.UTF_8).size <= MediaHandoff.MAXIMUM_TITLE_BYTES)
        assertTrue(trimmed.all { it == 'é' })
    }

    @Test
    fun `a blank url is how the television is told to play what was just sent`() {
        val command = MediaHandoff.playPushedFile("holiday.mp4", "video/mp4", durationMs = 90_000)
        assertEquals(MediaAction.LOAD, command.action)
        assertEquals("", command.url)
        assertEquals("holiday.mp4", command.title)
        assertEquals("video/mp4", command.mimeType)
        assertEquals(90_000, command.durationMs)
    }

    @Test
    fun `a command is never built with a mime type the wire will reject`() {
        assertFailsWith<IllegalArgumentException> {
            MediaHandoff.playPushedFile("holiday.mp4", "video/" + "x".repeat(300))
        }
    }

    @Test
    fun `clearing returns the television to its idle screen`() {
        val command = MediaHandoff.clear()
        assertEquals(MediaAction.CLEAR, command.action)
        assertEquals("", command.url)
    }
}
