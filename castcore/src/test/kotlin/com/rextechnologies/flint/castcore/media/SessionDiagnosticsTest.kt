package com.rextechnologies.flint.castcore.media

import com.rextechnologies.flint.protocol.media.LinkHealth
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class SessionDiagnosticsTest {
    @Test
    fun `a fresh session has heard nothing and has no ceiling`() {
        val fresh = SessionDiagnostics.initial(8_000_000, 20_000_000, 20_000_000, ThermalLevel.NONE)
        assertEquals(SessionDiagnostics.NOTHING_HEARD, fresh.lastDecision)
        assertFalse(fresh.ceilingApplied)
        assertEquals(0, fresh.receiverQueueDepth)
    }

    @Test
    fun `a ceiling below the session maximum is a ceiling, and one above it is refused`() {
        val held = SessionDiagnostics.initial(8_000_000, 14_000_000, 20_000_000, ThermalLevel.MODERATE)
        assertTrue(held.ceilingApplied)
        assertFailsWith<IllegalArgumentException> {
            SessionDiagnostics.initial(8_000_000, 21_000_000, 20_000_000, ThermalLevel.NONE)
        }
    }

    @Test
    fun `the decision line is the word and the rate, in the units the strip uses`() {
        assertEquals("Congested: 5.6 Mbit/s", SessionDiagnostics.decisionLine(LinkHealth.Congested, 5_600_000))
        assertEquals("Headroom: 20.0 Mbit/s", SessionDiagnostics.decisionLine(LinkHealth.Headroom, 20_000_000))
    }

    @Test
    fun `bytes are shown in the unit that keeps the number short`() {
        assertEquals("512 B", SessionDiagnostics.bytes(512))
        assertEquals("64 KiB", SessionDiagnostics.bytes(64 * 1_024))
        assertEquals("1.5 MiB", SessionDiagnostics.bytes(3L * 512 * 1_024))
    }

    @Test
    fun `every thermal level has its own word and none of them is a number`() {
        val words = ThermalLevel.entries.map { SessionDiagnostics.thermalWord(it) }
        assertEquals(words.size, words.toSet().size)
        words.forEach { assertTrue(it.none { character -> character.isDigit() }, it) }
    }

    @Test
    fun `negative counts and a blank decision are refused`() {
        assertFailsWith<IllegalArgumentException> {
            SessionDiagnostics(1, 1, 1, -1, 0, 0, "x", ThermalLevel.NONE)
        }
        assertFailsWith<IllegalArgumentException> {
            SessionDiagnostics(1, 1, 1, 0, 0, 0, " ", ThermalLevel.NONE)
        }
    }
}
