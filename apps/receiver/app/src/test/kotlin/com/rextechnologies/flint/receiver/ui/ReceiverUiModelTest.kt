package com.rextechnologies.flint.receiver.ui

import com.rextechnologies.flint.receiver.ReceiverNetworkState
import com.rextechnologies.flint.receiver.ReceiverUiState
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

class ReceiverUiModelTest {
    @Test
    fun `pairing code is split into two non-wrapping groups`() {
        assertEquals(PairingCodeGroups("697", "096", "6 9 7 0 9 6"), pairingCodeGroups("697096"))
    }

    @Test
    fun `pairing code preserves leading zeroes`() {
        assertEquals(PairingCodeGroups("004", "283", "0 0 4 2 8 3"), pairingCodeGroups("004283"))
    }

    @Test
    fun `widest decimal code remains valid`() {
        assertEquals(PairingCodeGroups("888", "888", "8 8 8 8 8 8"), pairingCodeGroups("888888"))
    }

    @Test
    fun `invalid code shapes never reach the pairing presentation`() {
        listOf("", "-----", "12345", "1234567", "123 456", "１２３４５６", "12A456", "------")
            .forEach { assertNull(pairingCodeGroups(it), "Unexpectedly accepted '$it'") }
    }

    @Test
    fun `idle experience reports each lifecycle state`() {
        assertEquals(IdleExperience.STARTING, ReceiverUiState().idleExperience())
        assertEquals(
            IdleExperience.NO_NETWORK,
            ReceiverUiState(networkState = ReceiverNetworkState.WAITING).idleExperience(),
        )
        assertEquals(IdleExperience.READY, readyState().idleExperience())
        assertEquals(IdleExperience.CONNECTED, readyState().copy(peerName = "PC").idleExperience())
        assertEquals(IdleExperience.ATTENTION, readyState().copy(error = "Port unavailable").idleExperience())
    }

    @Test
    fun `error takes precedence over an old connected peer`() {
        val state = readyState().copy(peerName = "Old PC", error = "Listener stopped")
        assertEquals(IdleExperience.ATTENTION, state.idleExperience())
    }

    @Test
    fun `blank peer names never create a connected state`() {
        assertFalse(readyState().copy(peerName = "").connected)
        assertFalse(readyState().copy(peerName = "   ").connected)
        assertTrue(readyState().copy(peerName = "Living room PC").connected)
    }

    @Test
    fun `display title removes a normal extension and filename separators`() {
        assertEquals(
            "New Adobe 2026 VPro blade3 1160x870",
            displayTitle("new_Adobe 2026_VPro-blade3_1160x870.mp4"),
        )
    }

    @Test
    fun `display title collapses whitespace and preserves meaningful dots`() {
        assertEquals("Series.episode 01", displayTitle("  series.episode__01.mkv  "))
        assertEquals(".profile", displayTitle(".profile"))
        assertEquals("Archive.tar", displayTitle("archive.tar.gz"))
    }

    @Test
    fun `display title uses its fallback for blank input and keeps long suffixes`() {
        assertEquals("Flint media", displayTitle("   ", "Flint media"))
        assertEquals("Recording.verylongextension", displayTitle("recording.verylongextension"))
    }

    @Test
    fun `endpoint is absent until a local address exists`() {
        assertNull(endpointLabel(ReceiverUiState()))
        assertEquals("10.0.0.8:47855", endpointLabel(readyState()))
    }

    @Test
    fun `progress handles unknown invalid and zero durations`() {
        assertEquals(0f, progressFraction(0, 0))
        assertEquals(0f, progressFraction(1_000, -1))
        assertEquals(0f, progressFraction(-1, 10_000))
    }

    @Test
    fun `progress is proportional and clamped`() {
        assertEquals(0.5f, progressFraction(5_000, 10_000))
        assertEquals(1f, progressFraction(20_000, 10_000))
        assertEquals(1f, progressFraction(Long.MAX_VALUE, 1))
        assertTrue(progressFraction(1, 3) in 0.3333f..0.3334f)
    }

    @Test
    fun `elapsed labels cover boundaries and long media`() {
        assertEquals("0:00", elapsedLabel(-1))
        assertEquals("0:00", elapsedLabel(0))
        assertEquals("0:59", elapsedLabel(59_999))
        assertEquals("1:00", elapsedLabel(60_000))
        assertEquals("59:59", elapsedLabel(3_599_999))
        assertEquals("1:00:00", elapsedLabel(3_600_000))
        assertEquals("25:01:01", elapsedLabel(90_061_000))
    }

    @Test
    fun `ready invariant requires listening address and no error`() {
        assertFalse(ReceiverUiState().ready)
        assertFalse(ReceiverUiState(networkState = ReceiverNetworkState.LISTENING).ready)
        assertFalse(ReceiverUiState(address = "10.0.0.8").ready)
        assertFalse(readyState().copy(error = "failure").ready)
        assertTrue(readyState().ready)
    }

    private fun readyState() = ReceiverUiState(
        networkState = ReceiverNetworkState.LISTENING,
        pairingCode = "123456",
        address = "10.0.0.8",
        port = 47855,
    )
}
