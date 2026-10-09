package com.rextechnologies.flint.receiver

import com.rextechnologies.flint.protocol.wire.ControlMessage
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.protocol.wire.TransportAction
import com.rextechnologies.flint.protocol.wire.TransportControl
import java.util.concurrent.atomic.AtomicLong

/**
 * Tells the host when the person stops its share with this TV's remote.
 *
 * Back and Stop clear whatever the host put on the screen. A played file tells the host through its
 * own player state, but a share has no player, so without this the host would carry on capturing
 * and sending a picture nobody can see. The host stops its share when it reads a Stop.
 */
internal class ShareStopNotice {
    private val sequence = AtomicLong()

    /** The message to send as [surfaceMode] is cleared by the remote, or null when it is not a share. */
    fun onCleared(surfaceMode: SurfaceMode): ControlMessage? =
        when (surfaceMode) {
            SurfaceMode.MIRROR, SurfaceMode.PRESENTATION ->
                ControlMessage(sequence.incrementAndGet(), TransportControl(TransportAction.STOP))
            else -> null
        }
}
