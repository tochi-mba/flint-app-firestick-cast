package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.castcore.capability.RECEIVER_MINIMUM_API_LEVEL
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.capability.canInstallReceiver
import com.rextechnologies.flint.castcore.capability.isAndroidBased
import com.rextechnologies.flint.castcore.capability.isTooOldForReceiver
import com.rextechnologies.flint.castcore.capability.lowestApiLevel
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

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

/**
 * The receiver's own floor, held against the generations the resolver can name.
 *
 * This exists because the two facts lived apart and disagreed: every Android generation was reported
 * as installable, and the receiver's manifest refuses anything below API 25. A Fire OS 5 device was
 * therefore offered an Install button whose only possible outcome was INSTALL_FAILED_OLDER_SDK.
 */
class ReceiverFloorTest {
    @Test
    fun `the floor here is the floor the receiver is actually built with`() {
        // gradle/libs.versions.toml: receiver-min-sdk. If that moves, this fails and the copy,
        // the verdicts and the site's device table all have to move with it.
        assertEquals(25, RECEIVER_MINIMUM_API_LEVEL)
    }

    @Test
    fun `every generation the resolver can name has an api level, and the two agree`() {
        val fromResolver = mapOf(
            22 to ReceiverPlatform.FIRE_OS_5,
            25 to ReceiverPlatform.FIRE_OS_6,
            28 to ReceiverPlatform.FIRE_OS_7,
            29 to ReceiverPlatform.FIRE_OS_8,
            31 to ReceiverPlatform.FIRE_OS_14,
            35 to ReceiverPlatform.FIRE_OS_16,
        )
        fromResolver.forEach { (level, platform) ->
            assertEquals(platform, FireOsPlatformResolver.resolve(true, level, "AFTKA"), "API $level")
            assertEquals(level, platform.lowestApiLevel, platform.displayLabel)
        }
        // Nothing that is not a generation claims a level.
        assertNull(ReceiverPlatform.UNKNOWN.lowestApiLevel)
        assertNull(ReceiverPlatform.VEGA.lowestApiLevel)
    }

    @Test
    fun `fire os 5 is android and still cannot take the receiver`() {
        assertTrue(ReceiverPlatform.FIRE_OS_5.isAndroidBased())
        assertFalse(ReceiverPlatform.FIRE_OS_5.canInstallReceiver())
        assertTrue(ReceiverPlatform.FIRE_OS_5.isTooOldForReceiver())
    }

    @Test
    fun `every other android generation can take it, and the two refusals are told apart`() {
        listOf(
            ReceiverPlatform.FIRE_OS_6,
            ReceiverPlatform.FIRE_OS_7,
            ReceiverPlatform.FIRE_OS_8,
            ReceiverPlatform.FIRE_OS_14,
            ReceiverPlatform.FIRE_OS_16,
        ).forEach { platform ->
            assertTrue(platform.canInstallReceiver(), platform.displayLabel)
            assertFalse(platform.isTooOldForReceiver(), platform.displayLabel)
        }
        // Vega is not Android, so it is refused for a different reason and must not be reported as
        // merely old; an unidentified device is refused for a third reason and is neither.
        assertFalse(ReceiverPlatform.VEGA.isTooOldForReceiver())
        assertFalse(ReceiverPlatform.VEGA.canInstallReceiver())
        assertFalse(ReceiverPlatform.UNKNOWN.isTooOldForReceiver())
        assertFalse(ReceiverPlatform.UNKNOWN.canInstallReceiver())
    }

    @Test
    fun `the setup card refuses a too-old television without offering a remedy`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_5,
            ReceiverInstallStage.NotInstalled,
            BundledReceiver("com.rextechnologies.flint.receiver", "0.1.0", 1, 7_340_032),
            "Fire TV Stick",
        )
        assertEquals(ReceiverInstallStage.Impossible, plan.stage)
        assertNull(plan.installAction)
        assertNull(plan.removeAction)
        assertNull(plan.identifyAction)
        // An impossibility carries no remedy, which ReceiverSetupPlan's own init enforces.
        assertNull(plan.remedy)
        assertTrue(plan.body.contains("Fire OS 5"), plan.body)
        assertTrue(plan.body.contains("older than"), plan.body)
    }
}
