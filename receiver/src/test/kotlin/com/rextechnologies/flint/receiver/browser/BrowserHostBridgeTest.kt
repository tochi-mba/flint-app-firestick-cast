package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserHostBridgeTest {
    @Test
    fun `clear data confirmation publishes a TV dialog and clears only after accept`() {
        val dialogs = mutableListOf<com.rextechnologies.flint.receiver.BrowserDialogUi?>()
        val coordinator = BrowserCoordinator()
        coordinator.handleOpen(1, 1, "https://example.com/")
        val bridge = BrowserHostBridge(
            coordinator = coordinator,
            publishDialog = { dialogs += it },
            sendOutbound = { true },
        )

        bridge.requestClearData(1)

        assertEquals(1, dialogs.size)
        assertEquals("CONFIRM", dialogs.single()?.kind)
        bridge.replyFromTv(accepted = true)
        assertNull(dialogs.last())
    }

    @Test
    fun `preview enable is a no-op without a publisher`() {
        val bridge = BrowserHostBridge(
            coordinator = BrowserCoordinator(),
            publishDialog = {},
            sendOutbound = { false },
        )
        bridge.setPreviewEnabled(true)
        bridge.setPreviewEnabled(false)
    }
}
