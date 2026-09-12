package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import kotlin.test.Test
import kotlin.test.assertEquals

class FireOsPlatformResolverTest {
    @Test
    fun `a known vega build model is vega before anything else is consulted`() {
        assertEquals(ReceiverPlatform.VEGA, FireOsPlatformResolver.resolve(true, 34, "AFTCA002"))
        assertEquals(ReceiverPlatform.VEGA, FireOsPlatformResolver.resolve(false, null, "aftcl001"))
        assertEquals(ReceiverPlatform.VEGA, FireOsPlatformResolver.resolve(true, 22, " AFTCA002 "))
    }

    @Test
    fun `a nickname that merely sounds like vega is not evidence`() {
        assertEquals(ReceiverPlatform.UNKNOWN, FireOsPlatformResolver.resolve(false, null, "Fire TV Select"))
        assertEquals(ReceiverPlatform.FIRE_OS_8, FireOsPlatformResolver.resolve(true, 30, "AFTCA002X"))
    }

    @Test
    fun `silence is unknown, not the likelier answer`() {
        assertEquals(ReceiverPlatform.UNKNOWN, FireOsPlatformResolver.resolve(false, 30, "AFTKA"))
        assertEquals(ReceiverPlatform.UNKNOWN, FireOsPlatformResolver.resolve(false, null, null))
    }

    @Test
    fun `every documented api level maps to its fire os generation`() {
        val expected = mapOf(
            22 to ReceiverPlatform.FIRE_OS_5,
            25 to ReceiverPlatform.FIRE_OS_6,
            28 to ReceiverPlatform.FIRE_OS_7,
            29 to ReceiverPlatform.FIRE_OS_8,
            30 to ReceiverPlatform.FIRE_OS_8,
            31 to ReceiverPlatform.FIRE_OS_14,
            32 to ReceiverPlatform.FIRE_OS_14,
            33 to ReceiverPlatform.FIRE_OS_14,
            34 to ReceiverPlatform.FIRE_OS_14,
            35 to ReceiverPlatform.FIRE_OS_16,
            36 to ReceiverPlatform.FIRE_OS_16,
        )
        expected.forEach { (level, platform) ->
            assertEquals(platform, FireOsPlatformResolver.resolve(true, level, "AFTKA"), "API $level")
        }
    }

    @Test
    fun `an undocumented level is unknown rather than rounded to a neighbour`() {
        listOf(null, 0, 21, 23, 24, 26, 27, 37, 99).forEach { level ->
            assertEquals(ReceiverPlatform.UNKNOWN, FireOsPlatformResolver.resolve(true, level, "AFTKA"), "API $level")
        }
    }
}
