package com.rextechnologies.flint.castcore.media

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ThermalPolicyTest {
    private val maximum = 10_000_000

    @Test
    fun `a cool phone is not told anything, because there is nothing to say`() {
        listOf(ThermalLevel.NONE, ThermalLevel.LIGHT).forEach { level ->
            val decision = ThermalPolicy.decide(level, maximum)
            assertEquals(maximum, decision.bitrateCeiling)
            assertEquals(60, decision.frameRateCeiling)
            assertFalse(decision.shouldStop)
            assertNull(decision.sentence)
        }
    }

    @Test
    fun `a warm phone degrades quietly but not silently`() {
        val decision = ThermalPolicy.decide(ThermalLevel.MODERATE, maximum)
        assertTrue(decision.bitrateCeiling < maximum)
        assertEquals(60, decision.frameRateCeiling)
        assertFalse(decision.shouldStop)
        assertNotNull(decision.sentence)
    }

    @Test
    fun `a hot phone drops the frame rate as well, and says the picture will look softer`() {
        val severe = ThermalPolicy.decide(ThermalLevel.SEVERE, maximum)
        assertTrue(severe.frameRateCeiling <= 30)
        assertTrue(assertNotNull(severe.sentence).contains("softer"))

        val critical = ThermalPolicy.decide(ThermalLevel.CRITICAL, maximum)
        assertTrue(critical.bitrateCeiling < severe.bitrateCeiling)
        assertTrue(critical.frameRateCeiling <= severe.frameRateCeiling)
        assertFalse(critical.shouldStop)
    }

    @Test
    fun `at the levels where android is already shedding load, flint stops and explains`() {
        listOf(ThermalLevel.EMERGENCY, ThermalLevel.SHUTDOWN).forEach { level ->
            val decision = ThermalPolicy.decide(level, maximum)
            assertTrue(decision.shouldStop)
            assertTrue(assertNotNull(decision.sentence).contains("too hot"))
        }
    }

    @Test
    fun `the ceilings are fractions of this session's maximum rather than absolutes`() {
        val large = ThermalPolicy.decide(ThermalLevel.MODERATE, 20_000_000).bitrateCeiling
        val small = ThermalPolicy.decide(ThermalLevel.MODERATE, 4_000_000).bitrateCeiling
        assertTrue(large > small, "a session that started conservatively is not told it may climb")
    }

    @Test
    fun `no ceiling ever falls below the controller's own floor`() {
        // Exercised well above the floor, so that a level failing to reach it is a real failure
        // rather than one hidden by a session maximum that is already the floor.
        listOf(ThermalPolicy.MINIMUM_BITRATE, 4_000_000, 20_000_000).forEach { sessionMaximum ->
            ThermalLevel.entries.forEach { level ->
                val decision = ThermalPolicy.decide(level, sessionMaximum)
                assertTrue(
                    decision.bitrateCeiling >= ThermalPolicy.MINIMUM_BITRATE,
                    "$level at $sessionMaximum",
                )
            }
        }
    }

    @Test
    fun `no ceiling ever rises above this session's maximum`() {
        // A ceiling is a cap, so heat may only ever lower it. Before this was clamped, a 500 kbit/s
        // session was handed a 1 Mbit/s "ceiling" at MODERATE -- twice what it agreed to send,
        // arrived at by getting warm.
        listOf(200_000, 500_000, 999_999, ThermalPolicy.MINIMUM_BITRATE, 4_000_000, 20_000_000)
            .forEach { sessionMaximum ->
                ThermalLevel.entries.forEach { level ->
                    val decision = ThermalPolicy.decide(level, sessionMaximum)
                    assertTrue(
                        decision.bitrateCeiling <= sessionMaximum,
                        "$level raised a $sessionMaximum session to ${decision.bitrateCeiling}",
                    )
                    assertTrue(
                        decision.frameRateCeiling <= 60,
                        "$level raised the frame rate to ${decision.frameRateCeiling}",
                    )
                }
            }
    }

    @Test
    fun `a session already below the floor is capped at its own maximum, not raised to the floor`() {
        val sessionMaximum = 500_000
        assertEquals(
            sessionMaximum,
            ThermalPolicy.decide(ThermalLevel.MODERATE, sessionMaximum).bitrateCeiling,
        )
        assertEquals(
            sessionMaximum,
            ThermalPolicy.decide(ThermalLevel.EMERGENCY, sessionMaximum).bitrateCeiling,
        )
    }

    @Test
    fun `the ceilings fall as the phone heats up, at every session size`() {
        listOf(2_000_000, 8_000_000, 20_000_000).forEach { sessionMaximum ->
            val ladder = listOf(
                ThermalLevel.NONE,
                ThermalLevel.MODERATE,
                ThermalLevel.SEVERE,
                ThermalLevel.CRITICAL,
            ).map { ThermalPolicy.decide(it, sessionMaximum).bitrateCeiling }
            assertEquals(
                ladder.sortedDescending(),
                ladder,
                "at $sessionMaximum the ceilings did not fall monotonically: $ladder",
            )
        }
    }

    @Test
    fun `a nonsensical session maximum is refused`() {
        assertFailsWith<IllegalArgumentException> { ThermalPolicy.decide(ThermalLevel.NONE, 0) }
        assertFailsWith<IllegalArgumentException> {
            ThermalPolicy.decide(ThermalLevel.NONE, maximum, sessionMaximumFrameRate = 0)
        }
        assertFailsWith<IllegalArgumentException> {
            ThermalPolicy.decide(ThermalLevel.NONE, maximum, sessionMaximumFrameRate = 121)
        }
    }

    @Test
    fun `a decision that contradicts itself is refused`() {
        assertFailsWith<IllegalArgumentException> { ThermalDecision(0, 30, false, null) }
        assertFailsWith<IllegalArgumentException> { ThermalDecision(100, 0, false, null) }
        assertFailsWith<IllegalArgumentException> { ThermalDecision(100, 30, false, " ") }
    }

    @Test
    fun `the levels mirror the platform's own, one for one`() {
        assertEquals(7, ThermalLevel.entries.size)
    }
}
