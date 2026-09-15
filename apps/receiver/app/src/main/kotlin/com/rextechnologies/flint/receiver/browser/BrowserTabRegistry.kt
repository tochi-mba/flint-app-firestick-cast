package com.rextechnologies.flint.receiver.browser

/** One open page. */
data class BrowserTab(
    val id: Long,
    val title: String = "",
    val url: String = "",
    val loading: Boolean = false,
    val progress: Int = 0,
    val canGoBack: Boolean = false,
    val canGoForward: Boolean = false,
    val faviconId: Long = 0,
    /** Whether this tab currently holds a renderer, as opposed to saved navigation state. */
    val live: Boolean = false,
)

data class BrowserTabsState(
    val tabs: List<BrowserTab> = emptyList(),
    val activeId: Long = 0,
) {
    val active: BrowserTab? get() = tabs.firstOrNull { it.id == activeId }
}

/** What the Android side has to do to make a decision real. */
sealed interface TabEffect {
    /** Build a renderer for a new tab. A null url means the new tab page. */
    data class Create(val id: Long, val url: String?) : TabEffect

    /** Put this tab's renderer on the glass. */
    data class Show(val id: Long) : TabEffect

    /** Save navigation state and release the renderer. The tab stays open. */
    data class Freeze(val id: Long) : TabEffect

    /** Rebuild a renderer from saved state. */
    data class Restore(val id: Long) : TabEffect

    /** The tab is gone; discard its renderer and its saved state. */
    data class Destroy(val id: Long) : TabEffect

    /** The cap was reached. Reported so the surface can say so rather than appearing to hang. */
    data object Refused : TabEffect
}

data class TabTransition(
    val state: BrowserTabsState,
    val effects: List<TabEffect>,
)

/**
 * Which pages are open, and which of them are allowed to hold a renderer.
 *
 * A Fire TV stick has one to two gigabytes for the whole system. Eight live WebViews is not a
 * budget decision, it is an out-of-memory kill — so at most two tabs keep a renderer (the one being
 * looked at, plus the most recently used, which makes flipping between two sites instant) and the
 * rest are frozen to saved navigation state.
 *
 * Pure, and it emits effects rather than performing them, so the freeze/restore/destroy ordering is
 * proven here instead of on hardware.
 */
class BrowserTabRegistry {
    companion object {
        /** Enough for how anyone actually browses on a television; far short of what would hurt. */
        const val MAX_TABS = 8

        /** Foreground plus one. The second slot is what makes flipping between two sites instant. */
        const val MAX_LIVE = 2
    }

    private var nextId: Long = 1

    /**
     * Most-recently-used order, newest first.
     *
     * Kept beside the state rather than inside it because it is an eviction detail, not something
     * the surface or the wire should ever see.
     */
    private val recency = ArrayDeque<Long>()

    fun open(state: BrowserTabsState, url: String?): TabTransition {
        if (state.tabs.size >= MAX_TABS) {
            return TabTransition(state, listOf(TabEffect.Refused))
        }

        val id = nextId
        nextId += 1
        val tab = BrowserTab(id = id, url = url.orEmpty(), live = true)
        val opened = state.copy(tabs = state.tabs + tab, activeId = id)

        val effects = mutableListOf<TabEffect>(TabEffect.Create(id, url))
        val (evicted, freezeEffects) = enforceLiveBudget(opened, keep = id)
        effects += freezeEffects
        effects += TabEffect.Show(id)
        return TabTransition(evicted, effects)
    }

    fun select(state: BrowserTabsState, id: Long): TabTransition {
        val tab = state.tabs.firstOrNull { it.id == id } ?: return TabTransition(state, emptyList())
        if (state.activeId == id) {
            return TabTransition(state, emptyList())
        }

        val effects = mutableListOf<TabEffect>()
        var next = state.copy(activeId = id)
        if (!tab.live) {
            effects += TabEffect.Restore(id)
            next = next.mapTab(id) { it.copy(live = true) }
        }
        touch(id)

        val (evicted, freezeEffects) = enforceLiveBudget(next, keep = id)
        effects += freezeEffects
        effects += TabEffect.Show(id)
        return TabTransition(evicted, effects)
    }

    fun close(state: BrowserTabsState, id: Long): TabTransition {
        val index = state.tabs.indexOfFirst { it.id == id }
        if (index < 0) {
            return TabTransition(state, emptyList())
        }

        val remaining = state.tabs.filterNot { it.id == id }
        recency.remove(id)
        val effects = mutableListOf<TabEffect>(TabEffect.Destroy(id))

        if (remaining.isEmpty()) {
            return TabTransition(BrowserTabsState(), effects)
        }

        if (state.activeId != id) {
            return TabTransition(state.copy(tabs = remaining), effects)
        }

        // The right-hand neighbour, which is where the eye already is; the left-hand one when the
        // closed tab was last in the row.
        val successor = remaining.getOrNull(index) ?: remaining.last()
        var next = state.copy(tabs = remaining, activeId = successor.id)
        if (!successor.live) {
            effects += TabEffect.Restore(successor.id)
            next = next.mapTab(successor.id) { it.copy(live = true) }
        }
        touch(successor.id)

        val (evicted, freezeEffects) = enforceLiveBudget(next, keep = successor.id)
        effects += freezeEffects
        effects += TabEffect.Show(successor.id)
        return TabTransition(evicted, effects)
    }

    fun update(state: BrowserTabsState, id: Long, change: (BrowserTab) -> BrowserTab): BrowserTabsState {
        if (state.tabs.none { it.id == id }) {
            return state
        }
        return state.mapTab(id, change)
    }

    /**
     * Frees renderers until only [MAX_LIVE] remain, never touching [keep].
     *
     * Least-recently-used first, so the tab someone is most likely to return to is the one that
     * survives.
     */
    private fun enforceLiveBudget(
        state: BrowserTabsState,
        keep: Long,
    ): Pair<BrowserTabsState, List<TabEffect>> {
        touch(keep)
        val live = state.tabs.filter { it.live }
        if (live.size <= MAX_LIVE) {
            return state to emptyList()
        }

        val order = recency.toList()
        val candidates = live
            .filter { it.id != keep }
            .sortedBy { tab ->
                val position = order.indexOf(tab.id)
                if (position < 0) Int.MAX_VALUE else position
            }
            .reversed()

        var next = state
        val effects = mutableListOf<TabEffect>()
        var remaining = live.size
        candidates.forEach { candidate ->
            if (remaining <= MAX_LIVE) return@forEach
            effects += TabEffect.Freeze(candidate.id)
            next = next.mapTab(candidate.id) { it.copy(live = false) }
            remaining -= 1
        }
        return next to effects
    }

    private fun touch(id: Long) {
        recency.remove(id)
        recency.addFirst(id)
    }

    private fun BrowserTabsState.mapTab(id: Long, change: (BrowserTab) -> BrowserTab) =
        copy(tabs = tabs.map { if (it.id == id) change(it) else it })
}
