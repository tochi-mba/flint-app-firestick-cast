package com.rextechnologies.flint.receiver.browser.net

import com.rextechnologies.flint.protocol.wire.BrowserCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserDialogReplyMessage
import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserLibraryStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserNetworkCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserProfileCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserTabCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserViewCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceResizeMessage
import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserStateMessage
import com.rextechnologies.flint.protocol.wire.BrowserDialogMessage
import com.rextechnologies.flint.protocol.wire.BrowserPreviewMessage
import com.rextechnologies.flint.protocol.wire.WireMessage

/** Authenticated browser-session callbacks owned by ReceiverService / BrowserCoordinator. */
interface BrowserSecureSessionListener {
    /** A paired TLS controller is now available as an optional, non-TV profile owner. */
    fun onSessionAuthenticated(
        sessionId: Long,
        deviceName: String,
        negotiatedProtocolVersion: Int,
    ) = onSessionAuthenticated(sessionId, deviceName)

    /** Backward-compatible overload for tests that omit the negotiated protocol version. */
    fun onSessionAuthenticated(sessionId: Long, deviceName: String) = Unit
    fun onCommand(command: BrowserCommandMessage)
    fun onInput(input: BrowserInputMessage)
    fun onDialogReply(reply: BrowserDialogReplyMessage)
    fun onTabCommand(command: BrowserTabCommandMessage) = Unit
    fun onViewCommand(command: BrowserViewCommandMessage) = Unit
    fun onLibraryCommand(command: BrowserLibraryCommandMessage) = Unit
    fun onLibraryState(state: BrowserLibraryStateMessage) = Unit
    fun onProfileCommand(command: BrowserProfileCommandMessage) = Unit
    fun onNetworkCommand(command: BrowserNetworkCommandMessage) = Unit
    fun onWorkspaceCommand(command: BrowserWorkspaceCommandMessage) = Unit
    fun onWorkspaceResize(input: BrowserWorkspaceResizeMessage) = Unit
    fun onWorkspaceInput(input: BrowserWorkspaceInputMessage) = Unit

    /** Session-keyed teardown prevents a stale controller from clearing a replacement. */
    fun onSessionEnded(sessionId: Long) = onSessionEnded()
    fun onSessionEnded()
}

/** Outbound browser frames that may only travel on the authenticated TLS writer. */
sealed interface BrowserOutboundMessage {
    data class State(val message: BrowserStateMessage) : BrowserOutboundMessage
    data class Preview(val message: BrowserPreviewMessage) : BrowserOutboundMessage
    data class Dialog(val message: BrowserDialogMessage) : BrowserOutboundMessage
    data class Raw(val message: WireMessage) : BrowserOutboundMessage
}
