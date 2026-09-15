package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * Several pages open at once, on a device with one or two gigabytes of memory.
 *
 * The constraint that shapes all of this: a Fire TV stick cannot hold eight live WebViews. So the
 * registry decides which tabs keep a renderer and which are frozen to saved navigation state, and
 * it says so as effects the Android side carries out. Getting that wrong is either an out-of-memory
 * kill or a tab that silently loses where it was.
 */
class BrowserTabRegistryTest {
    private val registry = BrowserTabRegistry()

    @Test
    fun `opening the first tab makes it active and live`() {
        val transition = registry.open(BrowserTabsState(), "https://example.test")

        assertEquals(1, transition.state.tabs.size)
        val tab = transition.state.tabs.single()
        assertEquals(tab.id, transition.state.activeId)
        assertTrue(transition.effects.any { it is TabEffect.Create && it.id == tab.id })
        assertTrue(transition.effects.any { it is TabEffect.Show && it.id == tab.id })
    }

    @Test
    fun `tab ids are never reused after a close`() {
        // A recycled id would let a stale effect land on a different page than the one it meant.
        var state = registry.open(BrowserTabsState(), "https://a.test").state
        val firstId = state.tabs.single().id
        state = registry.close(state, firstId).state

        state = registry.open(state, "https://b.test").state

        assertTrue(state.tabs.single().id > firstId)
    }

    @Test
    fun `only the foreground tab and one recent tab stay live`() {
        var state = BrowserTabsState()
        repeat(4) { state = registry.open(state, "https://s$it.test").state }

        val live = state.tabs.filter { it.live }

        assertEquals(BrowserTabRegistry.MAX_LIVE, live.size)
        assertTrue(live.any { it.id == state.activeId }, "the foreground tab must be live")
    }

    @Test
    fun `pushing a tab out of the live set freezes it rather than dropping it`() {
        var state = registry.open(BrowserTabsState(), "https://a.test").state
        val first = state.tabs.single().id
        state = registry.open(state, "https://b.test").state
        val second = state.tabs.last().id

        val transition = registry.open(state, "https://c.test")

        assertTrue(
            transition.effects.any { it is TabEffect.Freeze && it.id == first },
            "the oldest live tab was not frozen: ${transition.effects}",
        )
        // Frozen, not closed. It is still a tab the viewer can go back to.
        assertTrue(transition.state.tabs.any { it.id == first })
        assertTrue(transition.state.tabs.any { it.id == second })
    }

    @Test
    fun `selecting a frozen tab restores it and frees room for it`() {
        var state = BrowserTabsState()
        repeat(3) { state = registry.open(state, "https://s$it.test").state }
        val frozen = state.tabs.first { !it.live }

        val transition = registry.select(state, frozen.id)

        assertEquals(frozen.id, transition.state.activeId)
        assertTrue(transition.state.tabs.first { it.id == frozen.id }.live)
        assertTrue(transition.effects.any { it is TabEffect.Restore && it.id == frozen.id })
        assertTrue(transition.effects.any { it is TabEffect.Show && it.id == frozen.id })
    }

    @Test
    fun `selecting a tab that is already live does not restore it again`() {
        var state = registry.open(BrowserTabsState(), "https://a.test").state
        val first = state.tabs.single().id
        state = registry.open(state, "https://b.test").state

        val transition = registry.select(state, first)

        assertTrue(transition.effects.none { it is TabEffect.Restore })
        assertTrue(transition.effects.any { it is TabEffect.Show && it.id == first })
    }

    @Test
    fun `selecting the active tab changes nothing at all`() {
        val state = registry.open(BrowserTabsState(), "https://a.test").state

        val transition = registry.select(state, state.activeId)

        assertEquals(state, transition.state)
        assertTrue(transition.effects.isEmpty())
    }

    @Test
    fun `the tab count is capped and the cap is reported rather than silently ignored`() {
        var state = BrowserTabsState()
        repeat(BrowserTabRegistry.MAX_TABS) { state = registry.open(state, "https://s$it.test").state }

        val transition = registry.open(state, "https://one.too.many.test")

        assertEquals(BrowserTabRegistry.MAX_TABS, transition.state.tabs.size)
        assertTrue(transition.effects.any { it is TabEffect.Refused })
    }

    @Test
    fun `closing the active tab moves to its right-hand neighbour`() {
        var state = BrowserTabsState()
        repeat(3) { state = registry.open(state, "https://s$it.test").state }
        val ids = state.tabs.map { it.id }
        state = registry.select(state, ids[1]).state

        val transition = registry.close(state, ids[1])

        assertEquals(ids[2], transition.state.activeId)
        assertTrue(transition.effects.any { it is TabEffect.Destroy && it.id == ids[1] })
    }

    @Test
    fun `closing the last tab in the row falls back to its left-hand neighbour`() {
        var state = BrowserTabsState()
        repeat(3) { state = registry.open(state, "https://s$it.test").state }
        val ids = state.tabs.map { it.id }
        state = registry.select(state, ids.last()).state

        val transition = registry.close(state, ids.last())

        assertEquals(ids[1], transition.state.activeId)
    }

    @Test
    fun `closing a background tab leaves the active one alone`() {
        var state = BrowserTabsState()
        repeat(3) { state = registry.open(state, "https://s$it.test").state }
        val activeBefore = state.activeId
        val other = state.tabs.first { it.id != activeBefore }.id

        val transition = registry.close(state, other)

        assertEquals(activeBefore, transition.state.activeId)
    }

    @Test
    fun `closing the only tab leaves nothing open`() {
        val state = registry.open(BrowserTabsState(), "https://a.test").state

        val transition = registry.close(state, state.activeId)

        assertTrue(transition.state.tabs.isEmpty())
        assertEquals(0, transition.state.activeId)
        assertTrue(transition.effects.any { it is TabEffect.Destroy })
    }

    @Test
    fun `closing something that is not open is not an event`() {
        val state = registry.open(BrowserTabsState(), "https://a.test").state

        val transition = registry.close(state, 9_999)

        assertEquals(state, transition.state)
        assertTrue(transition.effects.isEmpty())
    }

    @Test
    fun `page updates land on the tab they belong to and nowhere else`() {
        var state = BrowserTabsState()
        repeat(2) { state = registry.open(state, "https://s$it.test").state }
        val target = state.tabs.first().id

        state = registry.update(state, target) {
            it.copy(title = "Example", url = "https://example.test/a", progress = 40, loading = true)
        }

        val updated = state.tabs.first { it.id == target }
        assertEquals("Example", updated.title)
        assertEquals(40, updated.progress)
        assertTrue(updated.loading)
        assertTrue(state.tabs.filter { it.id != target }.all { it.title.isEmpty() })
    }

    @Test
    fun `updating a tab that has gone is harmless`() {
        val state = registry.open(BrowserTabsState(), "https://a.test").state

        assertEquals(state, registry.update(state, 9_999) { it.copy(title = "no") })
    }

    @Test
    fun `the active tab can always be found, or is plainly absent`() {
        assertNull(BrowserTabsState().active)

        val state = registry.open(BrowserTabsState(), "https://a.test").state
        assertEquals(state.activeId, state.active?.id)
    }

    @Test
    fun `a fresh tab with no address is a new tab page rather than a blank`() {
        val transition = registry.open(BrowserTabsState(), url = null)

        assertTrue(transition.state.tabs.single().url.isEmpty())
        assertTrue(transition.effects.any { it is TabEffect.Create && it.url == null })
    }

    @Test
    fun `tab order follows the order they were opened in`() {
        var state = BrowserTabsState()
        repeat(4) { state = registry.open(state, "https://s$it.test").state }

        assertEquals(state.tabs.map { it.id }.sorted(), state.tabs.map { it.id })
    }

    @Test
    fun `every transition leaves at most the allowed number of live tabs`() {
        // The invariant the memory budget rests on, checked across a long mixed sequence rather
        // than at one convenient moment.
        var state = BrowserTabsState()
        repeat(BrowserTabRegistry.MAX_TABS) { state = registry.open(state, "https://s$it.test").state }
        state.tabs.map { it.id }.forEach { id ->
            state = registry.select(state, id).state
            assertTrue(
                state.tabs.count { it.live } <= BrowserTabRegistry.MAX_LIVE,
                "too many live after selecting $id",
            )
        }
        state.tabs.map { it.id }.forEach { id ->
            state = registry.close(state, id).state
            assertTrue(
                state.tabs.count { it.live } <= BrowserTabRegistry.MAX_LIVE,
                "too many live after closing $id",
            )
        }
        assertTrue(state.tabs.isEmpty())
    }

    @Test
    fun `a frozen tab is never shown while it is still frozen`() {
        var state = BrowserTabsState()
        repeat(3) { state = registry.open(state, "https://s$it.test").state }

        state.tabs.filter { !it.live }.forEach { frozen ->
            assertFalse(frozen.id == state.activeId, "a frozen tab was left in the foreground")
        }
    }
}
