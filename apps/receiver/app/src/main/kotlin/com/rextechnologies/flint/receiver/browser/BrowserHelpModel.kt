package com.rextechnologies.flint.receiver.browser

/** Device-local help state. Opening is always explicit and completion never disables replay. */
internal data class BrowserHelpProgress(val open: Boolean = false, val index: Int = 0, val dismissed: Boolean = false) {
    init { require(index in BrowserHelpTopics.entries.indices) }
    fun show() = copy(open = true)
    fun hide() = copy(open = false)
    fun back() = copy(index = (index - 1).coerceAtLeast(0))
    fun next(): BrowserHelpProgress = if (index < BrowserHelpTopics.entries.lastIndex) copy(index = index + 1)
        else complete()
    fun complete() = BrowserHelpProgress(open = false, dismissed = true)
}

internal data class BrowserHelpTopic(val title: String, val body: String)

internal object BrowserHelpTopics {
    val entries = listOf(
        BrowserHelpTopic("Start here", "Choose Browse on this TV to browse without a computer. To use Windows, pair from Cast using the code on this screen, then open Web. Previously trusted browser connections reconnect when their endpoint and pairing code are available."),
        BrowserHelpTopic("Your remote", "Use the D-pad to move between controls and Select to activate one. In page interaction, directions move the pointer and Select clicks. Back returns to the controls before leaving the workspace. Address / search opens the remote keyboard."),
        BrowserHelpTopic("Profiles", "TV profiles save bookmarks, history and workspace pages here. A connected-device profile is temporary on the TV. TV site sign-ins are shared: changing a profile does not create a separate cookie jar."),
        BrowserHelpTopic("Independent webpages", "Each workspace pane is a separate webpage. Select a pane to navigate or control it. Page fullscreen fills that pane. The receiver limits live pages to its supported capacity; suspended pages may reload when selected."),
        BrowserHelpTopic("Media and VPN", "Play/Pause requests playback control; a site may decline it. Mute works only when confirmed by the renderer. VPN belongs to a TV profile; auto-connect is on by default when enabled, and Android may ask for permission. Check status before browsing; a failed connection is not proof that traffic is protected."),
        BrowserHelpTopic(
            "Free WireGuard config",
            "Flint does not run a free VPN service. For zero-cost egress, create a free-tier VPS, install WireGuard, open UDP 51820, export a peer config with AllowedIPs 0.0.0.0/0, then paste it from Windows under Web → MORE → TV NETWORK / VPN. Full-tunnel is fine — this TV keeps the local control network outside the tunnel.",
        ),
    )
}
