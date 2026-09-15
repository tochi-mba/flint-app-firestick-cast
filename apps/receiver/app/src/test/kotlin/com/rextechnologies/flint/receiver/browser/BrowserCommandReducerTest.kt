package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertNull

class BrowserCommandReducerTest {
    private val reducer = BrowserCommandReducer()
    private val url = (BrowserUrlPolicy().evaluate("https://example.com/start") as BrowserUrlResult.Accepted).url

    @Test
    fun `open navigate close form one strictly ordered browser epoch`() {
        val opened = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(10, 1, url))
        assertIs<BrowserCommandEffect.Open>(opened.effect)
        assertEquals(BrowserSurfaceOwner.BROWSER, opened.state.surface)

        val navigated = reducer.reduce(opened.state, BrowserCommand.Navigate(10, 2, url))
        assertIs<BrowserCommandEffect.Navigate>(navigated.effect)
        assertEquals(2, navigated.state.lastCommandId)
        assertEquals(2, navigated.state.navigationId)

        val closed = reducer.reduce(navigated.state, BrowserCommand.Close(10, 3))
        assertIs<BrowserCommandEffect.Close>(closed.effect)
        assertEquals(BrowserSurfaceOwner.IDLE, closed.state.surface)
        assertNull(closed.state.activeEpoch)
        assertEquals(10, closed.state.observedEpoch)
    }

    @Test
    fun `TV can open a blank browser without inventing a public homepage`() {
        val opened = reducer.reduce(BrowserCommandState(), BrowserCommand.OpenBlank(10, 1))

        assertIs<BrowserCommandEffect.OpenBlank>(opened.effect)
        assertEquals(BrowserSurfaceOwner.BROWSER, opened.state.surface)
        assertEquals(10, opened.state.activeEpoch)
        assertEquals(1, opened.state.navigationId)
    }

    @Test
    fun `profile switch resets to blank on the active ordered command stream`() {
        val opened = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(10, 1, url))

        val reset = reducer.reduce(opened.state, BrowserCommand.ResetBlank(10, 2))

        assertIs<BrowserCommandEffect.ResetBlank>(reset.effect)
        assertEquals(2, reset.state.lastCommandId)
        assertEquals(2, reset.state.navigationId)
        assertEquals(
            BrowserCommandRejection.STALE_COMMAND,
            assertIs<BrowserCommandEffect.Rejected>(
                reducer.reduce(reset.state, BrowserCommand.Navigate(10, 2, url)).effect,
            ).reason,
        )
    }

    @Test
    fun `cockpit control shares the browser command watermark without changing navigation`() {
        val opened = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(10, 1, url))

        val controlled = reducer.reduce(opened.state, BrowserCommand.Control(10, 2))

        assertIs<BrowserCommandEffect.Control>(controlled.effect)
        assertEquals(2, controlled.state.lastCommandId)
        assertEquals(1, controlled.state.navigationId)
        assertEquals(
            BrowserCommandRejection.STALE_COMMAND,
            assertIs<BrowserCommandEffect.Rejected>(
                reducer.reduce(controlled.state, BrowserCommand.Navigate(10, 2, url)).effect,
            ).reason,
        )
    }

    @Test
    fun `cockpit control requires the active epoch and strictly newer id`() {
        val opened = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(10, 4, url)).state

        listOf(
            BrowserCommand.Control(9, 5),
            BrowserCommand.Control(11, 5),
            BrowserCommand.Control(10, 4),
            BrowserCommand.Control(0, 5),
        ).forEach { command ->
            assertIs<BrowserCommandEffect.Rejected>(reducer.reduce(opened, command).effect)
        }
    }

    @Test
    fun `duplicate stale and future commands never produce a browser port effect`() {
        val opened = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(10, 5, url)).state
        val commands = listOf(
            BrowserCommand.Navigate(10, 5, url),
            BrowserCommand.Navigate(9, 6, url),
            BrowserCommand.Navigate(11, 6, url),
            BrowserCommand.Close(10, 4),
        )

        commands.forEach { command ->
            val transition = reducer.reduce(opened, command)
            assertIs<BrowserCommandEffect.Rejected>(transition.effect)
            assertEquals(opened, transition.state)
        }
    }

    @Test
    fun `open requires a newer epoch after close`() {
        val opened = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(4, 1, url)).state
        val closed = reducer.reduce(opened, BrowserCommand.Close(4, 2)).state

        val stale = reducer.reduce(closed, BrowserCommand.Open(4, 3, url))
        assertEquals(
            BrowserCommandRejection.STALE_EPOCH,
            assertIs<BrowserCommandEffect.Rejected>(stale.effect).reason,
        )

        val newer = reducer.reduce(closed, BrowserCommand.Open(5, 1, url))
        assertIs<BrowserCommandEffect.Open>(newer.effect)
        assertEquals(5, newer.state.activeEpoch)
    }

    @Test
    fun `newer host open reclaims a live browser left without close`() {
        // Defect seen on device 2026-09-07: Windows sent Open(google) after reconnect while the TV
        // still held a prior browser tab (surface=BROWSER). OPEN was refused SURFACE_BUSY, Windows
        // still treated the wire send as success, and the search never appeared on screen.
        val orphan = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(4, 1, url)).state
        assertEquals(BrowserSurfaceOwner.BROWSER, orphan.surface)

        val reclaim = reducer.reduce(orphan, BrowserCommand.Open(5, 1, url))

        assertIs<BrowserCommandEffect.Open>(reclaim.effect)
        assertEquals(BrowserSurfaceOwner.BROWSER, reclaim.state.surface)
        assertEquals(5, reclaim.state.activeEpoch)
        assertEquals(1, reclaim.state.lastCommandId)
        assertEquals(1, reclaim.state.navigationId)
    }

    @Test
    fun `newer openBlank reclaims a live browser surface`() {
        val orphan = reducer.reduce(BrowserCommandState(), BrowserCommand.OpenBlank(4, 1)).state
        val reclaim = reducer.reduce(orphan, BrowserCommand.OpenBlank(9, 1))

        assertIs<BrowserCommandEffect.OpenBlank>(reclaim.effect)
        assertEquals(9, reclaim.state.activeEpoch)
    }

    @Test
    fun `reclaim open refuses stale or equal epochs on a live browser`() {
        val live = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(10, 3, url)).state

        listOf(
            BrowserCommand.Open(10, 4, url),
            BrowserCommand.Open(9, 1, url),
        ).forEach { command ->
            val rejected = reducer.reduce(live, command)
            assertEquals(
                BrowserCommandRejection.STALE_EPOCH,
                assertIs<BrowserCommandEffect.Rejected>(rejected.effect).reason,
                "command=$command",
            )
            assertEquals(live, rejected.state)
        }

        // Zero identifiers are rejected before surface/epoch reclaim logic runs.
        val invalid = reducer.reduce(live, BrowserCommand.Open(0, 1, url))
        assertEquals(
            BrowserCommandRejection.INVALID_IDENTIFIER,
            assertIs<BrowserCommandEffect.Rejected>(invalid.effect).reason,
        )
    }

    @Test
    fun `reclaim open never displaces mirror player or presentation`() {
        listOf(
            BrowserSurfaceOwner.MIRROR,
            BrowserSurfaceOwner.PLAYER,
            BrowserSurfaceOwner.PRESENTATION,
        ).forEach { owner ->
            val busy = BrowserCommandState(surface = owner, observedEpoch = 1)
            val rejected = reducer.reduce(busy, BrowserCommand.Open(100, 1, url))
            assertEquals(
                BrowserCommandRejection.SURFACE_BUSY,
                assertIs<BrowserCommandEffect.Rejected>(rejected.effect).reason,
                "owner=$owner",
            )
            assertEquals(busy, rejected.state)
        }
    }

    @Test
    fun `reclaim openBlank never displaces cast surfaces`() {
        val mirror = BrowserCommandState(surface = BrowserSurfaceOwner.MIRROR, observedEpoch = 1)
        val rejected = reducer.reduce(mirror, BrowserCommand.OpenBlank(100, 1))
        assertEquals(
            BrowserCommandRejection.SURFACE_BUSY,
            assertIs<BrowserCommandEffect.Rejected>(rejected.effect).reason,
        )
    }

    @Test
    fun `after reclaim navigate uses the new epoch only`() {
        val orphan = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(4, 1, url)).state
        val reclaim = reducer.reduce(orphan, BrowserCommand.Open(5, 1, url)).state

        val staleNavigate = reducer.reduce(reclaim, BrowserCommand.Navigate(4, 2, url))
        assertIs<BrowserCommandEffect.Rejected>(staleNavigate.effect)

        val ok = reducer.reduce(reclaim, BrowserCommand.Navigate(5, 2, url))
        assertIs<BrowserCommandEffect.Navigate>(ok.effect)
        assertEquals(2, ok.state.navigationId)
    }

    @Test
    fun `browser cannot claim a surface already owned by mirror or player`() {
        val mirror = BrowserCommandState(surface = BrowserSurfaceOwner.MIRROR)
        val rejected = reducer.reduce(mirror, BrowserCommand.Open(1, 1, url))
        val reason = assertIs<BrowserCommandEffect.Rejected>(rejected.effect).reason
        assertEquals(BrowserCommandRejection.SURFACE_BUSY, reason)

        val claimed = reducer.claimExternalSurface(BrowserCommandState(), BrowserSurfaceOwner.PLAYER)
        assertIs<BrowserCommandEffect.SurfaceChanged>(claimed.effect)
        assertEquals(BrowserSurfaceOwner.PLAYER, claimed.state.surface)
        val browserClaim = reducer.claimExternalSurface(claimed.state, BrowserSurfaceOwner.BROWSER)
        assertEquals(
            BrowserCommandRejection.SURFACE_BUSY,
            assertIs<BrowserCommandEffect.Rejected>(browserClaim.effect).reason,
        )
    }

    @Test
    fun `abandoning a dead session hands the surface back`() {
        // The host's transport can die without ever sending CLOSE — the desktop quits, the laptop
        // sleeps. Whatever the reason, the surface has to come back or nothing can open again.
        val open = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(4, 9, url))
        assertEquals(BrowserSurfaceOwner.BROWSER, open.state.surface)

        val abandoned = reducer.abandon(open.state)

        assertEquals(BrowserSurfaceOwner.IDLE, abandoned.state.surface)
        assertNull(abandoned.state.activeEpoch)
        assertIs<BrowserCommandEffect.SurfaceChanged>(abandoned.effect)
    }

    @Test
    fun `a session that ended can be followed by a new one`() {
        // The defect this pins, seen on the television: after the first host disconnected, every
        // later session's OPEN came back SURFACE_BUSY and the TV showed the dead session's page
        // until the app was force-stopped.
        val first = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(1, 1, url))
        val released = reducer.abandon(first.state)

        val second = reducer.reduce(released.state, BrowserCommand.Open(2, 1, url))

        assertIs<BrowserCommandEffect.Open>(second.effect)
        assertEquals(BrowserSurfaceOwner.BROWSER, second.state.surface)
        assertEquals(2, second.state.activeEpoch)
    }

    @Test
    fun `abandoning keeps the dead session's epoch out in the cold`() {
        // Cleanup, not a reset. A command replayed from the session that just died must still be
        // refused, so the observed epoch survives.
        val open = reducer.reduce(BrowserCommandState(), BrowserCommand.Open(7, 3, url))
        val released = reducer.abandon(open.state)

        val replayed = reducer.reduce(released.state, BrowserCommand.Open(7, 4, url))

        assertEquals(
            BrowserCommandRejection.STALE_EPOCH,
            assertIs<BrowserCommandEffect.Rejected>(replayed.effect).reason,
        )
    }

    @Test
    fun `abandoning a surface the browser never held changes nothing`() {
        // A session can end before it ever opened anything, and it must not evict a mirror or
        // player that legitimately owns the screen.
        val mirror = BrowserCommandState(surface = BrowserSurfaceOwner.MIRROR)

        val abandoned = reducer.abandon(mirror)

        assertEquals(mirror, abandoned.state)
        assertIs<BrowserCommandEffect.Noop>(abandoned.effect)
    }

    @Test
    fun `bad identifiers are rejected safely`() {
        val values = listOf(
            BrowserCommand.Open(0, 1, url),
            BrowserCommand.Open(1, 0, url),
        )

        values.forEach { command ->
            assertIs<BrowserCommandEffect.Rejected>(reducer.reduce(BrowserCommandState(), command).effect)
        }
    }
}
