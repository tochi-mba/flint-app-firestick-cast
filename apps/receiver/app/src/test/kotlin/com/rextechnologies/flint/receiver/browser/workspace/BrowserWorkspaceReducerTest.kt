package com.rextechnologies.flint.receiver.browser.workspace

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserWorkspaceReducerTest {
    private val reducer = BrowserWorkspaceReducer()

    @Test
    fun `resize retains renderers and survives layout changes and local restore`() {
        val before = openTwoPanes().state
        val split = BrowserWorkspaceSplit.of(7000, 3000)
        val resized = reduce(before, BrowserWorkspaceAction.SetSplit(split))
        assertEquals(before.panes, resized.state.panes)
        assertEquals(split, resized.state.split)
        assertTrue(resized.effects.all { it is BrowserWorkspaceEffect.LayoutChanged })
        val stacked = reduce(resized.state, BrowserWorkspaceAction.SetLayout(BrowserWorkspaceLayout.SPLIT_VERTICAL))
        assertEquals(split, stacked.state.split)
        val restored = reduce(
            BrowserWorkspaceAction.ActivateProfile(
                before.profile!!,
                stacked.state.snapshotForPersistence(),
            ),
        )
        assertEquals(split, restored.state.split)
    }

    @Test
    fun `resizing during theater is refused without changing geometry`() {
        val before = openTwoPanes().state.copy(theaterPaneId = 1)
        val result = reduce(before, BrowserWorkspaceAction.SetSplit(BrowserWorkspaceSplit.of(7000, 3000)))
        assertEquals(before, result.state)
        assertEquals(
            BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE,
            assertIs<BrowserWorkspaceEffect.Refused>(result.effects.single()).reason,
        )
    }

    @Test
    fun `open pane seeds a live renderer under a local profile`() {
        val activated = reduce(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("family")))
        val opened = reduce(activated.state, BrowserWorkspaceAction.OpenPane("https://example.com/a"))

        assertEquals(1, opened.state.panes.size)
        assertEquals(1L, opened.state.focusedPaneId)
        assertEquals(BrowserWorkspaceLayout.SINGLE, opened.state.layout)
        assertTrue(opened.state.pane(1)!!.rendererResidency.hasRenderer)
        assertIs<BrowserWorkspaceEffect.CreateRenderer>(
            opened.effects.first { it is BrowserWorkspaceEffect.CreateRenderer },
        )
    }

    @Test
    fun `second pane switches to side-by-side and keeps both under the live budget`() {
        val one = openTwoPanes()
        assertEquals(BrowserWorkspaceLayout.SPLIT_HORIZONTAL, one.state.layout)
        assertEquals(2, one.state.livePaneCount)
        assertEquals(2L, one.state.focusedPaneId)
    }

    @Test
    fun `third pane is refused under conservative Fire TV capacity`() {
        val two = openTwoPanes().state
        val refused = reduce(two, BrowserWorkspaceAction.OpenPane("https://example.com/c"))
        assertEquals(
            BrowserWorkspaceRefusal.PANE_CAPACITY_REACHED,
            assertIs<BrowserWorkspaceEffect.Refused>(refused.effects.single()).reason,
        )
        assertEquals(2, refused.state.panes.size)
    }

    @Test
    fun `side by side is refused when only one pane is open`() {
        val one = openOnePane().state
        val refused = reduce(one, BrowserWorkspaceAction.SetLayout(BrowserWorkspaceLayout.SPLIT_HORIZONTAL))
        assertEquals(
            BrowserWorkspaceRefusal.LAYOUT_DOES_NOT_FIT_PANES,
            assertIs<BrowserWorkspaceEffect.Refused>(refused.effects.single()).reason,
        )
    }

    @Test
    fun `grid layout is refused until capacity verifies it`() {
        val two = openTwoPanes().state
        val refused = reduce(two, BrowserWorkspaceAction.SetLayout(BrowserWorkspaceLayout.GRID_2X2))
        assertEquals(
            BrowserWorkspaceRefusal.LAYOUT_NOT_AVAILABLE,
            assertIs<BrowserWorkspaceEffect.Refused>(refused.effects.single()).reason,
        )
    }

    @Test
    fun `device profile cannot restore a persisted snapshot`() {
        val refused = reduce(
            BrowserWorkspaceAction.ActivateProfile(
                profile = BrowserWorkspaceProfile.ConnectedDevice(connectionId = 9),
                snapshot = BrowserWorkspaceSnapshot(
                    ownerProfileId = "family",
                    layout = BrowserWorkspaceLayout.SINGLE,
                    focusedPaneId = 1,
                    nextPaneId = 2,
                    panes = listOf(
                        BrowserWorkspaceSavedPane(
                            id = 1,
                            slot = 0,
                            page = BrowserWorkspacePage(url = "https://example.com"),
                            desiredMuted = false,
                        ),
                    ),
                ),
            ),
        )
        assertEquals(
            BrowserWorkspaceRefusal.DEVICE_PROFILE_CANNOT_RESTORE_SNAPSHOT,
            assertIs<BrowserWorkspaceEffect.Refused>(refused.effects.single()).reason,
        )
    }

    @Test
    fun `disconnect device wipes ephemeral workspace`() {
        val device = reduce(
            BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.ConnectedDevice(7)),
        )
        val opened = reduce(device.state, BrowserWorkspaceAction.OpenPane(null))
        val cleared = reduce(opened.state, BrowserWorkspaceAction.DisconnectDevice(7))
        assertNull(cleared.state.profile)
        assertTrue(cleared.state.panes.isEmpty())
    }

    @Test
    fun `only focused pane may enter fullscreen and another pane cannot steal it`() {
        val two = reduce(openTwoPanes().state, BrowserWorkspaceAction.FocusPane(1)).state
        val first = reduce(
            two,
            BrowserWorkspaceAction.SetPageFullscreen(paneId = 1, rendererGeneration = 1, active = true),
        )
        assertEquals(1L, first.state.pageFullscreenPaneId)

        val second = reduce(
            first.state,
            BrowserWorkspaceAction.SetPageFullscreen(paneId = 2, rendererGeneration = 1, active = true),
        )
        assertEquals(1L, second.state.pageFullscreenPaneId)
        assertEquals(
            BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE,
            assertIs<BrowserWorkspaceEffect.Refused>(second.effects.single()).reason,
        )
    }

    @Test
    fun `mute request does not invent observed playback state`() {
        val one = reduce(
            reduce(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("family"))).state,
            BrowserWorkspaceAction.OpenPane("https://example.com"),
        )
        val muted = reduce(one.state, BrowserWorkspaceAction.SetPaneMuted(1, muted = true))
        assertTrue(muted.state.pane(1)!!.media.desiredMuted)
        assertEquals(
            BrowserWorkspacePlaybackObservation.UNKNOWN,
            muted.state.pane(1)!!.media.observedPlayback,
        )
    }

    @Test
    fun `play pause request stays pending until the host reports completion`() {
        val one = reduce(
            reduce(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("family"))).state,
            BrowserWorkspaceAction.OpenPane("https://example.com"),
        )
        val requested = reduce(one.state, BrowserWorkspaceAction.RequestMediaPlayPause(1))
        assertEquals(
            BrowserWorkspacePlaybackRequestStatus.PENDING_DISPATCH,
            requested.state.pane(1)!!.media.playbackRequest!!.status,
        )
        assertEquals(
            BrowserWorkspacePlaybackObservation.UNKNOWN,
            requested.state.pane(1)!!.media.observedPlayback,
        )
    }

    @Test
    fun `local snapshot round-trips layout focus and mute preference`() {
        val two = openTwoPanes().state
        val muted = reduce(two, BrowserWorkspaceAction.SetPaneMuted(1, muted = true)).state
        val snapshot = muted.snapshotForPersistence()!!
        val restored = reduce(
            BrowserWorkspaceAction.ActivateProfile(
                profile = BrowserWorkspaceProfile.LocalTv("family"),
                snapshot = snapshot,
            ),
        )
        assertEquals(2, restored.state.panes.size)
        assertTrue(restored.state.pane(1)!!.media.desiredMuted)
        assertEquals(snapshot.focusedPaneId, restored.state.focusedPaneId)
    }

    @Test
    fun `randomized sequences never duplicate pane ids or exceed capacity`() {
        val actions = listOf(
            BrowserWorkspaceAction.OpenPane("https://example.com/1"),
            BrowserWorkspaceAction.OpenPane("https://example.com/2"),
            BrowserWorkspaceAction.OpenPane("https://example.com/3"),
            BrowserWorkspaceAction.FocusPane(1),
            BrowserWorkspaceAction.FocusPane(2),
            BrowserWorkspaceAction.SetLayout(BrowserWorkspaceLayout.SPLIT_VERTICAL),
            BrowserWorkspaceAction.SetLayout(BrowserWorkspaceLayout.GRID_2X2),
            BrowserWorkspaceAction.ClosePane(1),
            BrowserWorkspaceAction.ClosePane(2),
            BrowserWorkspaceAction.SetPaneMuted(1, true),
            BrowserWorkspaceAction.RequestMediaPlayPause(2),
            BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE),
            BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.WORKSPACE_CHROME),
            BrowserWorkspaceAction.EnterTheaterMode(1),
            BrowserWorkspaceAction.ExitTheaterMode,
        )
        var state = reduce(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("soak"))).state
        repeat(250) { round ->
            val action = actions[round % actions.size]
            state = reduce(state, action).state
            assertEquals(state.panes.map { it.id }.distinct().size, state.panes.size)
            assertTrue(state.panes.size <= BrowserWorkspaceCapacity.CONSERVATIVE_FIRE_TV.maxOpenPanes)
            assertTrue(state.livePaneCount <= BrowserWorkspaceCapacity.CONSERVATIVE_FIRE_TV.maxLiveRenderers)
            if (state.panes.isNotEmpty()) {
                assertTrue(state.panes.any { it.id == state.focusedPaneId })
            } else {
                assertEquals(BrowserWorkspaceState.NO_PANE_ID, state.focusedPaneId)
            }
            assertFalse(state.pageFullscreenPaneId != null && state.theaterPaneId != null)
        }
    }

    @Test
    fun `a dead renderer fails its pane and leaves page mode until the pane is focused again`() {
        val opened = openOnePane().state
        val generation = opened.pane(1)!!.rendererGeneration
        val inPage = reduce(opened, BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE))

        val failed = reduce(inPage.state, rendererGone(1, generation))

        val pane = failed.state.pane(1)!!
        assertEquals(BrowserWorkspaceRendererResidency.FAILED, pane.rendererResidency)
        assertEquals(BrowserWorkspaceRendererFailure.RENDERER_PROCESS_GONE, pane.rendererFailure)
        assertEquals(BrowserWorkspaceInteractionMode.WORKSPACE_CHROME, failed.state.interactionMode)
        assertIs<BrowserWorkspaceEffect.DestroyPane>(failed.effects.first())

        val refocused = reduce(failed.state, BrowserWorkspaceAction.FocusPane(1)).state.pane(1)!!
        assertTrue(refocused.rendererResidency.hasRenderer)
        assertEquals(generation + 1, refocused.rendererGeneration)
        assertNull(refocused.rendererFailure)
    }

    @Test
    fun `a renderer failure from another generation or for a pane without a renderer is ignored`() {
        val opened = openOnePane().state
        val generation = opened.pane(1)!!.rendererGeneration
        assertEquals(opened, reduce(opened, rendererGone(1, generation + 1)).state)
        val failed = reduce(opened, rendererGone(1, generation)).state
        assertEquals(failed, reduce(failed, rendererGone(1, generation)).state)
    }

    @Test
    fun `a dead renderer in theater ends theater mode`() {
        val theater = openTwoPanes().state.copy(theaterPaneId = 1)
        val failed = reduce(theater, rendererGone(1, theater.pane(1)!!.rendererGeneration))
        assertNull(failed.state.theaterPaneId)
        assertTrue(failed.effects.any { it is BrowserWorkspaceEffect.TheaterModeChanged })
    }

    private fun rendererGone(paneId: Long, generation: Long) = BrowserWorkspaceAction.RendererFailed(
        paneId = paneId,
        rendererGeneration = generation,
        reason = BrowserWorkspaceRendererFailure.RENDERER_PROCESS_GONE,
    )

    private fun openOnePane(): BrowserWorkspaceTransition {
        val activated = reduce(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("family")))
        return reduce(activated.state, BrowserWorkspaceAction.OpenPane("https://example.com/a"))
    }

    private fun openTwoPanes(): BrowserWorkspaceTransition {
        val activated = reduce(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv("family")))
        val first = reduce(activated.state, BrowserWorkspaceAction.OpenPane("https://example.com/a"))
        return reduce(first.state, BrowserWorkspaceAction.OpenPane("https://example.com/b"))
    }

    private fun reduce(action: BrowserWorkspaceAction) = reduce(BrowserWorkspaceState(), action)

    private fun reduce(state: BrowserWorkspaceState, action: BrowserWorkspaceAction) =
        reducer.reduce(state, action)
}
