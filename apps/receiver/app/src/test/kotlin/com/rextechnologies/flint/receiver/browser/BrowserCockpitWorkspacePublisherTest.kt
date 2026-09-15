package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserWorkspaceStateMessage
import com.rextechnologies.flint.protocol.wire.WireCodec
import com.rextechnologies.flint.protocol.wire.WireFrame
import com.rextechnologies.flint.protocol.wire.WireMessage
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceAction
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceCapacity
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceController
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceProfile
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceState
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue
import kotlin.test.fail

class BrowserCockpitWorkspacePublisherTest {
    private val capacity = BrowserWorkspaceCapacity.CONSERVATIVE_FIRE_TV

    @Test fun `blank live pane url is published as about blank so wire accepts mosaic`() {
        val sent = mutableListOf<WireMessage>()
        val publisher = BrowserCockpitPublisher { sent.add(it) }
        val controller = BrowserWorkspaceController()
        controller.dispatch(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("family")))
        controller.dispatch(BrowserWorkspaceAction.OpenPane(null))
        assertTrue(publisher.publishWorkspace(BrowserState(epoch = 7), controller.state, capacity))
        val opened = sent.single() as BrowserWorkspaceStateMessage
        assertEquals("about:blank", opened.panes.single().url)
        val frame = WireFrame(protocolVersion = 3, message = opened)
        assertEquals(frame, WireCodec.decode(WireCodec.encode(frame)))
    }

    @Test fun `closed workspace publishes an empty monotonic wire snapshot`() {
        val sent = mutableListOf<WireMessage>()
        val publisher = BrowserCockpitPublisher { sent.add(it) }
        val controller = BrowserWorkspaceController()
        controller.dispatch(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("family")))
        controller.dispatch(BrowserWorkspaceAction.OpenPane(null))
        assertTrue(publisher.publishWorkspace(BrowserState(epoch = 7), controller.state, capacity))
        controller.dispatch(BrowserWorkspaceAction.DeactivateProfile)
        assertTrue(publisher.publishWorkspace(BrowserState(epoch = 7), controller.state, capacity))
        val opened = sent.first() as BrowserWorkspaceStateMessage
        val closed = sent.last() as BrowserWorkspaceStateMessage
        assertTrue(opened.panes.isNotEmpty())
        assertTrue(closed.panes.isEmpty())
        assertEquals(0L, closed.focusedPaneId)
        assertTrue(closed.revision > opened.revision)
        val frame = WireFrame(protocolVersion = 3, message = closed)
        assertEquals(frame, WireCodec.decode(WireCodec.encode(frame)))
    }

    @Test fun `no browser epoch does not publish or consume a revision`() {
        val publisher = BrowserCockpitPublisher { fail("No active browser epoch") }
        for (epoch in listOf(null, 0L, -1L)) {
            assertFalse(publisher.publishWorkspace(BrowserState(epoch = epoch), BrowserWorkspaceState(), capacity))
        }
        assertEquals(0L, publisher.currentWorkspaceRevision)
    }

    @Test fun `writer rejection remains visible and subsequent snapshots have new revisions`() {
        val revisions = mutableListOf<Long>()
        val publisher = BrowserCockpitPublisher {
            revisions.add((it as BrowserWorkspaceStateMessage).revision)
            false
        }
        repeat(2) {
            assertFalse(publisher.publishWorkspace(BrowserState(epoch = 7), BrowserWorkspaceState(), capacity))
        }
        assertEquals(listOf(1L, 2L), revisions)
    }
}
