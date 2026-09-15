package com.rextechnologies.flint.protocol.http

import kotlin.test.Test
import kotlin.test.assertEquals

class MimeTypesTest {
    @Test
    fun `known extensions are case insensitive and ignore query fragments`() {
        val cases = mapOf(
            "movie.MP4" to "video/mp4",
            "/folder/clip.mkv?token=1#ignored" to "video/x-matroska",
            "C:\\media\\song.FLAC" to "audio/flac",
            "captions.srt" to "application/x-subrip",
            "photo.avif" to "image/avif",
            "manifest.m3u8" to "application/vnd.apple.mpegurl",
            "manifest.mpd" to "application/dash+xml",
            "page.html" to "text/html; charset=utf-8",
        )
        cases.forEach { (name, expected) -> assertEquals(expected, MimeTypes.forFileName(name), name) }
    }

    @Test
    fun `unknown absent and empty extensions use safe binary default`() {
        listOf("README", "file.", ".hidden", "thing.unknown", "", "/folder/").forEach {
            assertEquals(MimeTypes.DEFAULT, MimeTypes.forFileName(it), it)
        }
    }
}
