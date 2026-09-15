package com.rextechnologies.flint.receiver.browser

/**
 * Keeps each tab's page state separate while the browser session keeps one command stream.
 *
 * [BrowserCoordinator] owns epoch and command ordering for the secure session. A tab switch must
 * not reset either, but it also must not let a late title or progress callback from a background
 * renderer overwrite the page on the glass. This class is the small boundary between those two
 * concerns: it stores one [BrowserState] per tab and lets the Android host execute only the emitted
 * [TabEffect]s.
 */
class BrowserTabSession(
    private val registry: BrowserTabRegistry = BrowserTabRegistry(),
    private val reducer: BrowserStateReducer = BrowserStateReducer(),
) {
    var state: BrowserTabsState = BrowserTabsState()
        private set

    private val pages = LinkedHashMap<Long, BrowserState>()

    /** Creates the first tab for an already accepted OPEN command. */
    fun start(page: BrowserState): TabTransition {
        if (state.tabs.isNotEmpty()) return TabTransition(state, emptyList())
        val transition = registry.open(state, page.address?.canonicalUrl)
        state = transition.state
        state.active?.let { pages[it.id] = page }
        refreshActive(page)
        return transition
    }

    /** Opens a blank tab (or a supplied address) without changing the session epoch. */
    fun open(currentPage: BrowserState, url: String? = null): TabTransition {
        saveActive(currentPage)
        val transition = registry.open(state, url)
        state = transition.state
        val created = transition.effects.filterIsInstance<TabEffect.Create>().firstOrNull()
        if (created != null) {
            pages[created.id] = blankPage(currentPage)
        }
        return transition
    }

    /** Opens a host-requested address after its shared command id has already been accepted. */
    fun openAccepted(currentPage: BrowserState, address: BrowserAddress?): TabTransition {
        saveActive(currentPage)
        val transition = registry.open(state, address?.canonicalUrl)
        state = transition.state
        transition.effects.filterIsInstance<TabEffect.Create>().firstOrNull()?.let { created ->
            pages[created.id] = acceptedPage(currentPage, address)
        }
        return transition
    }

    /** Duplicates a tab's address into a fresh renderer; private form/history is never copied. */
    fun duplicate(currentPage: BrowserState, sourceId: Long): TabTransition {
        val source = pages[sourceId] ?: return TabTransition(state, emptyList())
        return openAccepted(currentPage, source.address)
    }

    fun select(currentPage: BrowserState, id: Long): TabTransition {
        saveActive(currentPage)
        val transition = registry.select(state, id)
        state = transition.state
        return transition
    }

    fun close(currentPage: BrowserState, id: Long): TabTransition {
        saveActive(currentPage)
        val transition = registry.close(state, id)
        if (transition.effects.any { it is TabEffect.Destroy && it.id == id }) {
            pages.remove(id)
        }
        state = transition.state
        return transition
    }

    /** Records the coordinator's authoritative state for the foreground tab. */
    fun recordActive(page: BrowserState) {
        val id = state.activeId
        if (id == 0L) return
        pages[id] = page
        refreshActive(page)
    }

    /** Reduces a callback from any renderer without letting it leak into another tab. */
    fun recordEvent(id: Long, event: BrowserStateEvent): BrowserState? {
        val before = pages[id] ?: return null
        val after = reducer.reduce(before, event)
        if (after == before) return before
        pages[id] = after
        refresh(id, after)
        return after
    }

    fun pageFor(id: Long): BrowserState? = pages[id]

    fun activePage(): BrowserState? = pages[state.activeId]

    /**
     * After a Windows TLS reconnect the coordinator watermark resets to 0. Tab-held page snapshots
     * still carry the previous host's lastAcceptedCommandId; without clamping, [activatePage]
     * refuses every select with "expired browser session" (2026-09-08).
     */
    fun clampCommandWatermark(maxAcceptedCommandId: Long) {
        if (pages.isEmpty()) return
        for (id in pages.keys.toList()) {
            val page = pages[id] ?: continue
            if (page.lastAcceptedCommandId > maxAcceptedCommandId) {
                pages[id] = page.copy(lastAcceptedCommandId = maxAcceptedCommandId)
            }
        }
    }

    fun setFavicon(id: Long, faviconId: Long) {
        state = registry.update(state, id) { it.copy(faviconId = faviconId.coerceAtLeast(0)) }
    }

    fun clear() {
        state = BrowserTabsState()
        pages.clear()
    }

    private fun saveActive(page: BrowserState) {
        if (state.activeId != 0L) {
            pages[state.activeId] = page
            refresh(state.activeId, page)
        }
    }

    private fun refreshActive(page: BrowserState) = refresh(state.activeId, page)

    private fun refresh(id: Long, page: BrowserState) {
        state = registry.update(state, id) { tab ->
            tab.copy(
                title = page.title?.value.orEmpty(),
                url = page.address?.canonicalUrl ?: tab.url,
                loading = page.phase == BrowserPhase.OPENING || page.phase == BrowserPhase.LOADING,
                progress = page.progressPercent.coerceIn(0, 100),
                canGoBack = page.canGoBack,
                canGoForward = page.canGoForward,
            )
        }
    }

    private fun blankPage(current: BrowserState): BrowserState = BrowserState(
        phase = BrowserPhase.READY,
        epoch = current.epoch,
        lastAcceptedCommandId = current.lastAcceptedCommandId,
    )

    private fun acceptedPage(current: BrowserState, address: BrowserAddress?): BrowserState = BrowserState(
        phase = if (address == null) BrowserPhase.READY else BrowserPhase.LOADING,
        epoch = current.epoch,
        navigationId = current.lastAcceptedCommandId,
        lastAcceptedCommandId = current.lastAcceptedCommandId,
        address = address,
    )
}
