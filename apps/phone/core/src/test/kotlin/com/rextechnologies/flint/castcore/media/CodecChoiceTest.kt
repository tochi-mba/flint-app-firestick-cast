package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.protocol.wire.CodecId
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class CodecChoiceTest {
    private val both = setOf(CodecId.H264, CodecId.H265)

    @Test
    fun `h264 comes first when both ends have both, and hevc is the fallback`() {
        assertEquals(listOf(CodecId.H264, CodecId.H265), CodecChoice.order(both, both))
    }

    @Test
    fun `the order is bounded by what the receiver decodes and what the phone encodes`() {
        assertEquals(listOf(CodecId.H265), CodecChoice.order(setOf(CodecId.H265), both))
        assertEquals(listOf(CodecId.H264), CodecChoice.order(both, setOf(CodecId.H264)))
        assertTrue(CodecChoice.order(setOf(CodecId.H265), setOf(CodecId.H264)).isEmpty())
        assertTrue(CodecChoice.order(emptySet(), both).isEmpty())
    }

    @Test
    fun `a codec neither side names as video is never chosen, whatever the set holds`() {
        // The hardware set can only ever hold video encoders, but the receiver's HELLO carries its
        // audio codecs in the same set, and an audio codec must not become the video choice.
        val withAudio = both + CodecId.AAC_LC + CodecId.OPUS
        assertEquals(listOf(CodecId.H264, CodecId.H265), CodecChoice.order(withAudio, withAudio))
        assertTrue(CodecChoice.order(setOf(CodecId.AAC_LC), setOf(CodecId.AAC_LC)).isEmpty())
    }

    @Test
    fun `the check before any receiver is known exercises the codec a session would try first`() {
        assertEquals(CodecId.H264, CodecChoice.preferred(both))
        assertEquals(CodecId.H265, CodecChoice.preferred(setOf(CodecId.H265)))
        assertNull(CodecChoice.preferred(emptySet()))
        assertNull(CodecChoice.preferred(setOf(CodecId.AV1)))
    }

    @Test
    fun `every codec has a name a person would recognise`() {
        assertEquals("H.264", CodecNames.label(CodecId.H264))
        assertEquals("H.265", CodecNames.label(CodecId.H265))
        assertEquals("AAC-LC", CodecNames.label(CodecId.AAC_LC))
        assertEquals("Opus", CodecNames.label(CodecId.OPUS))
        assertEquals("AV1", CodecNames.label(CodecId.AV1))
        assertEquals("Codec 9", CodecNames.label(CodecId(9)))
    }
}
