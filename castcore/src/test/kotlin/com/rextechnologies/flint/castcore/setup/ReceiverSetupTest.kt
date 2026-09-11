package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private fun bundle(
    packageName: String = "com.rextechnologies.flint.receiver",
    versionName: String = "0.1.0",
    versionCode: Long = 1,
    sizeBytes: Long = 7_340_032,
) = BundledReceiver(packageName, versionName, versionCode, sizeBytes)

class BundledReceiverTest {
    @Test
    fun `a package nobody can name is refused`() {
        assertFailsWith<IllegalArgumentException> { bundle(packageName = " ") }
        assertFailsWith<IllegalArgumentException> { bundle(versionName = " ") }
        assertFailsWith<IllegalArgumentException> { bundle(versionCode = -1) }
        assertFailsWith<IllegalArgumentException> { bundle(sizeBytes = 0) }
    }

    @Test
    fun `a debug package is recognised as one`() {
        assertTrue(bundle(packageName = "com.rextechnologies.flint.receiver.debug").isDebugPackage)
        assertFalse(bundle().isDebugPackage)
    }

    @Test
    fun `the size is shown in the units a person thinks in`() {
        assertEquals("7.0 MB", bundle(sizeBytes = 7_340_032).sizeLabel)
        assertEquals("0.5 MB", bundle(sizeBytes = 524_288).sizeLabel)
    }
}

class ReceiverSetupPlanTest {
    @Test
    fun `nothing is offered for installation without saying what it is`() {
        assertFailsWith<IllegalArgumentException> {
            ReceiverSetupPlan(
                stage = ReceiverInstallStage.NOT_INSTALLED,
                headline = "Install",
                body = "Body.",
                installAction = "Install",
            )
        }
    }

    @Test
    fun `an impossibility cannot carry a remedy`() {
        assertFailsWith<IllegalArgumentException> {
            ReceiverSetupPlan(
                stage = ReceiverInstallStage.IMPOSSIBLE,
                headline = "No",
                body = "Body.",
                remedy = "Try something.",
            )
        }
    }

    @Test
    fun `a plan without words is refused`() {
        assertFailsWith<IllegalArgumentException> {
            ReceiverSetupPlan(ReceiverInstallStage.UNKNOWN, " ", "Body.")
        }
        assertFailsWith<IllegalArgumentException> {
            ReceiverSetupPlan(ReceiverInstallStage.UNKNOWN, "Headline", " ")
        }
    }
}

class ReceiverSetupTest {
    @Test
    fun `vega is refused plainly, whatever stage anything thinks it is at`() {
        ReceiverInstallStage.entries.forEach { stage ->
            val plan = ReceiverSetup.plan(ReceiverPlatform.VEGA, stage, bundle(), "Fire TV")
            assertEquals(ReceiverInstallStage.IMPOSSIBLE, plan.stage)
            assertNull(plan.remedy)
            assertNull(plan.installAction)
            assertNull(plan.removeAction)
            assertTrue(plan.body.contains("not Android"), plan.body)
        }
    }

    @Test
    fun `nothing is offered for a television that has not been identified`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.UNKNOWN,
            ReceiverInstallStage.NOT_INSTALLED,
            bundle(),
            "Fire TV",
        )
        assertEquals(ReceiverInstallStage.UNKNOWN, plan.stage)
        assertNull(plan.installAction)
        assertNotNull(plan.remedy)
        assertTrue(plan.body.contains("read-only"), plan.body)
    }

    @Test
    fun `removal is offered wherever installation is, on the same screen`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.NOT_INSTALLED,
            bundle(),
            "Fire TV Stick",
        )
        assertEquals(ReceiverSetup.INSTALL_ACTION, plan.installAction)
        assertEquals(ReceiverSetup.REMOVE_ACTION, plan.removeAction)
        assertTrue(plan.disclosure.isNotEmpty())
    }

    @Test
    fun `what will be installed is named before anything is installed`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.NOT_INSTALLED,
            bundle(),
            "Fire TV Stick",
        )
        assertTrue(plan.disclosure.first().startsWith("Package: "), plan.disclosure.toString())
        assertTrue(plan.disclosure.any { it.contains("0.1.0") })
        assertTrue(plan.disclosure.any { it.contains("47855") })
        assertFalse(plan.disclosure.any { it.contains(".debug") })
    }

    @Test
    fun `a debug package says so rather than hiding it`() {
        val debug = bundle(packageName = "com.rextechnologies.flint.receiver.debug")
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.NOT_INSTALLED,
            debug,
            "Fire TV Stick",
        )
        assertTrue(plan.disclosure.any { it.contains(".debug") }, plan.disclosure.toString())
    }

    @Test
    fun `a build with no bundled receiver explains itself rather than offering nothing silently`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.NOT_INSTALLED,
            bundled = null,
            deviceName = "Fire TV Stick",
        )
        assertNull(plan.installAction)
        assertNull(plan.removeAction)
        assertNotNull(plan.remedy)
        assertTrue(plan.headline.contains("No receiver is bundled"), plan.headline)
    }

    @Test
    fun `the television's own prompt is waited for rather than worked around`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_7,
            ReceiverInstallStage.AWAITING_AUTHORISATION,
            bundle(),
            "Fire TV Stick",
        )
        assertNull(plan.installAction)
        assertNotNull(plan.remedy)
        assertTrue(assertNotNull(plan.remedy).contains("TV remote"), assertNotNull(plan.remedy))
        assertTrue(plan.body.contains("TV owner's say"), plan.body)
    }

    @Test
    fun `installing says what is happening to the television while it happens`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_7,
            ReceiverInstallStage.INSTALLING,
            bundle(),
            "Fire TV Stick",
        )
        assertEquals(ReceiverInstallStage.INSTALLING, plan.stage)
        assertTrue(plan.headline.contains("Fire TV Stick"))
        assertNull(plan.installAction)
    }

    @Test
    fun `an installed receiver names its version and still offers removal`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.INSTALLED,
            bundle(versionName = "0.2.1"),
            "Fire TV Stick",
        )
        assertTrue(plan.body.contains("0.2.1"), plan.body)
        assertEquals(ReceiverSetup.REMOVE_ACTION, plan.removeAction)
        assertNull(plan.installAction)
    }

    @Test
    fun `an installed debug receiver explains the suffix rather than leaving it a mystery`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.INSTALLED,
            bundle(packageName = "com.rextechnologies.flint.receiver.debug"),
            "Fire TV Stick",
        )
        assertTrue(plan.body.contains(".debug"), plan.body)
    }

    @Test
    fun `an installed receiver with nothing bundled still reports itself installed`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.INSTALLED,
            bundled = null,
            deviceName = "Fire TV Stick",
        )
        assertEquals(ReceiverInstallStage.INSTALLED, plan.stage)
        assertEquals(ReceiverSetup.REMOVE_ACTION, plan.removeAction)
        assertTrue(plan.disclosure.isEmpty())
    }

    @Test
    fun `a failure carries the television's own account of it, and offers a retry`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.FAILED,
            bundle(),
            "Fire TV Stick",
            failureDetail = "There is not enough space on the device.",
        )
        assertEquals("There is not enough space on the device.", plan.body)
        assertEquals(ReceiverSetup.RETRY_ACTION, plan.installAction)
        assertEquals(ReceiverSetup.REMOVE_ACTION, plan.removeAction)
        assertNotNull(plan.remedy)
    }

    @Test
    fun `a failure with nothing to say says that rather than inventing a cause`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.FAILED,
            bundle(),
            "Fire TV Stick",
        )
        assertTrue(plan.body.contains("did not say why"), plan.body)
    }

    @Test
    fun `a failure with nothing bundled cannot offer a retry it could not honour`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.FAILED,
            bundled = null,
            deviceName = "Fire TV Stick",
        )
        assertNull(plan.installAction)
    }

    @Test
    fun `a device that is provably not android has nothing to install`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_8,
            ReceiverInstallStage.IMPOSSIBLE,
            bundle(),
            "Something",
        )
        assertEquals(ReceiverInstallStage.IMPOSSIBLE, plan.stage)
        assertNull(plan.remedy)
    }

    @Test
    fun `an authorised but not yet installed television is offered the install`() {
        val plan = ReceiverSetup.plan(
            ReceiverPlatform.FIRE_OS_16,
            ReceiverInstallStage.AUTHORISED,
            bundle(),
            "Fire TV",
        )
        assertEquals(ReceiverInstallStage.NOT_INSTALLED, plan.stage)
        assertEquals(ReceiverSetup.INSTALL_ACTION, plan.installAction)
    }
}
