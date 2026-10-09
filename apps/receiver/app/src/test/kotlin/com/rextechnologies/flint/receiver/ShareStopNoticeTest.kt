package com.rextechnologies.flint.receiver

import com.rextechnologies.flint.protocol.wire.ControlMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.protocol.wire.TransportAction
import com.rextechnologies.flint.protocol.wire.TransportControl
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** What the host is told when the remote clears its share. */
class ShareStopNoticeTest {
    @Test
    fun `clearing a share tells the host to stop, numbered in order`() {
        val notice = ShareStopNotice()

        val stop = TransportControl(TransportAction.STOP)

        assertEquals(ControlMessage(1, stop), notice.onCleared(SurfaceMode.MIRROR))
        assertEquals(ControlMessage(2, stop), notice.onCleared(SurfaceMode.PRESENTATION))
    }

    @Test
    fun `clearing anything else says nothing, since a played file reports its own state`() {
        val notice = ShareStopNotice()

        for (mode in listOf(SurfaceMode.IDLE, SurfaceMode.PLAYER, SurfaceMode.BROWSER)) {
            assertNull(notice.onCleared(mode), mode.name)
        }
        assertEquals(1, notice.onCleared(SurfaceMode.MIRROR)?.sequenceNumber, "nothing used a number")
    }
}
