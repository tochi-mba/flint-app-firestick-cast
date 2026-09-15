package com.rextechnologies.flint.receiver.browser

/**
 * Turns a persisted TV browsing session into the navigations a cold start must issue.
 *
 * A blank current page (Browse, profile switch) reloads the saved strip and may re-enter workspace
 * mode. An already-opened URL (typed from idle) keeps that page in front and appends the other
 * saved tabs, without forcing workspace mode.
 */
internal data class TvSessionRestorePlan(
    val navigateCurrentTo: String? = null,
    val extraUrls: List<String> = emptyList(),
    val activeIndex: Int = 0,
    val workspaceMode: Boolean = false,
) {
    val isEmpty: Boolean get() = navigateCurrentTo == null && extraUrls.isEmpty()
}

internal object TvSessionRestore {
    fun plan(session: BrowserTvBrowsingSession?, currentUrl: String?): TvSessionRestorePlan {
        val pages = session?.tabs.orEmpty().map { it.url.trim() }.filter { it.isNotEmpty() }
        if (pages.isEmpty()) {
            return TvSessionRestorePlan()
        }
        val current = currentUrl?.trim().orEmpty()
        if (current.isEmpty()) {
            val count = pages.size.coerceAtMost(BrowserTabRegistry.MAX_TABS)
            val active = session!!.activeTab.coerceIn(0, count - 1)
            return TvSessionRestorePlan(
                navigateCurrentTo = pages[0],
                extraUrls = pages.drop(1).take(BrowserTabRegistry.MAX_TABS - 1),
                activeIndex = active,
                workspaceMode = session.workspaceMode,
            )
        }
        return TvSessionRestorePlan(
            extraUrls = pages
                .filter { !it.equals(current, ignoreCase = true) }
                .take(BrowserTabRegistry.MAX_TABS - 1),
        )
    }
}
