package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * A tunnel that goes away has to take the pages with it, but only when it was promised.
 */
class BrowserVpnLossPolicyTest {
    @Test
    fun `losing a verified tunnel closes pages when the profile required one`() {
        // The case the setting exists for: an open page keeps fetching, and those requests would
        // leave over whatever network remains.
        assertTrue(
            BrowserVpnLossPolicy.shouldClosePages(
                wasConnected = true,
                isConnected = false,
                requiresVpnBeforeBrowse = true,
            ),
        )
    }

    @Test
    fun `a profile that only prefers VPN keeps its pages`() {
        // Auto-connect without require-VPN is a convenience, not a promise. Closing pages on it
        // would be a surprise rather than a protection.
        assertFalse(
            BrowserVpnLossPolicy.shouldClosePages(
                wasConnected = true,
                isConnected = false,
                requiresVpnBeforeBrowse = false,
            ),
        )
    }

    @Test
    fun `a profile that was never connected has nothing to lose`() {
        assertFalse(
            BrowserVpnLossPolicy.shouldClosePages(
                wasConnected = false,
                isConnected = false,
                requiresVpnBeforeBrowse = true,
            ),
        )
    }

    @Test
    fun `staying connected closes nothing`() {
        // Guards against re-evaluating on unrelated churn - a settings edit re-emits state, and
        // treating that as a loss would close pages under someone mid-browse.
        assertFalse(
            BrowserVpnLossPolicy.shouldClosePages(
                wasConnected = true,
                isConnected = true,
                requiresVpnBeforeBrowse = true,
            ),
        )
    }

    @Test
    fun `reconnecting after a loss does not close again`() {
        assertFalse(
            BrowserVpnLossPolicy.shouldClosePages(
                wasConnected = false,
                isConnected = true,
                requiresVpnBeforeBrowse = true,
            ),
        )
    }
}
