package com.rextechnologies.flint.receiver

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * The one line of text on the receiver's ongoing notification.
 *
 * It matters more than its size suggests: on a television the notification shade is where someone
 * looks when the on-screen pairing panel has been covered by whatever app is in front, so this line
 * is sometimes the only place the address and code are visible.
 */
class ReceiverNotificationTextTest {
    @Test
    fun `an addressed receiver prints where it is and how to pair with it`() {
        val text = receiverNotificationText(
            ReceiverUiState(address = "192.168.1.51", port = 47_855, pairingCode = "272390"),
        )

        assertEquals("192.168.1.51:47855 · code 272390", text)
    }

    @Test
    fun `the port is printed plainly rather than grouped`() {
        // A thousands separator here would be a genuine defect: nobody can type 47,855 into a
        // host field, and the state holds it as an Int where the default formatting is a real risk.
        val text = receiverNotificationText(ReceiverUiState(address = "10.0.0.4", port = 47_855))

        assertTrue(text.contains("10.0.0.4:47855"), text)
    }

    @Test
    fun `a receiver with no address says what it is waiting for`() {
        // The state during startup and after the network drops. An empty or half-built address
        // would read as a broken receiver rather than one waiting on a network.
        val text = receiverNotificationText(ReceiverUiState(address = null))

        assertEquals("Waiting for the hotspot network", text)
        assertTrue(text.isNotBlank())
    }
}
