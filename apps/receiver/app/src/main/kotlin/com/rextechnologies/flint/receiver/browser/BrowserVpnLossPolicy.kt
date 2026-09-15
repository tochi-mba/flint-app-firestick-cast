package com.rextechnologies.flint.receiver.browser

/**
 * What a lost tunnel means for pages that are already open.
 *
 * Blocking the next navigation is not the same as protecting the session. A page that is already
 * loaded keeps fetching — images, XHR, video segments — and those requests leave over whatever
 * network is left once the tunnel is gone. A profile that asked for a tunnel *before* browsing has
 * therefore not been served by a banner change alone.
 *
 * Stated as a predicate rather than inlined in a state collector so the rule can be read, and
 * tested, on its own.
 */
internal object BrowserVpnLossPolicy {
    /**
     * Whether open pages must be closed.
     *
     * Only a transition out of a verified connection counts. A profile that was never connected has
     * nothing to lose, and re-evaluating on every emission would close pages on unrelated state
     * churn such as a settings edit.
     */
    fun shouldClosePages(
        wasConnected: Boolean,
        isConnected: Boolean,
        requiresVpnBeforeBrowse: Boolean,
    ): Boolean = wasConnected && !isConnected && requiresVpnBeforeBrowse
}
