package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse

class VpnCapabilityProbeTest {
    @Test
    fun `null context is not preparable`() {
        val capability = AndroidVpnCapabilityProbe(context = null).probe()

        assertFalse(capability.preparable)
        assertEquals("No Android context", capability.reason)
    }

    @Test
    fun `fixed probe returns the configured capability`() {
        val expected = VpnCapability(preparable = true, reason = "granted")
        assertEquals(expected, FixedVpnCapabilityProbe(expected).probe())
    }
}
