package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserNetworkAction
import com.rextechnologies.flint.protocol.wire.BrowserNetworkCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserProfileAction
import com.rextechnologies.flint.protocol.wire.BrowserProfileCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserTabAction
import com.rextechnologies.flint.protocol.wire.BrowserTabCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserViewAction
import com.rextechnologies.flint.protocol.wire.BrowserViewCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserVpnProvider
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

class BrowserRemoteCommandHandlerTest {
    @Test
    fun `new tab address uses the accepted shared command id`() {
        val harness = Harness()

        harness.handler.handle(
            BrowserTabCommandMessage(7, 2, BrowserTabAction.NEW, 0, "https://two.test/"),
        )

        assertEquals("https://two.test/", harness.tabs.activePage()?.address?.canonicalUrl)
        assertEquals(2, harness.tabs.activePage()?.lastAcceptedCommandId)
        assertTrue(harness.tabTransitions > 0)
    }

    @Test
    fun `select tab applies a transition for the requested id`() {
        val harness = Harness()
        harness.handler.handle(BrowserTabCommandMessage(7, 2, BrowserTabAction.NEW, 0, null))
        val secondId = harness.tabs.state.tabs.last().id

        harness.handler.handle(BrowserTabCommandMessage(7, 3, BrowserTabAction.SELECT, secondId))

        assertEquals(secondId, harness.tabs.state.activeId)
        assertTrue(harness.tabTransitions >= 2)
    }

    @Test
    fun `view operation and profile operation share ordering`() {
        val harness = Harness()

        harness.handler.handle(BrowserViewCommandMessage(7, 2, BrowserViewAction.SET_ZOOM, 150))
        harness.handler.handle(
            BrowserProfileCommandMessage(7, 3, BrowserProfileAction.SELECT_TV_PROFILE, "kids"),
        )

        assertEquals(150, harness.view.zoomPercent)
        assertEquals("kids", harness.selectedTvProfile)
        assertEquals(3, harness.coordinator.snapshot().lastAcceptedCommandId)
    }

    @Test
    fun `network set with empty config keeps previous blob`() {
        val harness = Harness()
        val previous = ProfileNetworkSettings(
            vpnEnabled = true,
            provider = VpnProvider.WIREGUARD,
            autoConnectOnBrowserStart = true,
            configText = "[Interface]\nPrivateKey = AAA=\n\n[Peer]\nPublicKey = BBB=\n",
        )
        harness.stored["family"] = previous

        harness.handler.handle(
            BrowserNetworkCommandMessage(
                epoch = 7,
                commandId = 2,
                action = BrowserNetworkAction.SET,
                profileId = "family",
                vpnEnabled = false,
                provider = BrowserVpnProvider.NONE,
                autoConnectOnBrowserStart = false,
                configText = "",
            ),
        )

        assertEquals(previous.configText, harness.stored["family"]?.configText)
        assertEquals(false, harness.stored["family"]?.vpnEnabled)
        assertEquals(listOf("family"), harness.publishedNetwork)
    }

    private class Harness {
        val coordinator = BrowserCoordinator()
        val tabs = BrowserTabSession()
        private val settings = BrowserViewSettings()
        var tabTransitions = 0
        var view = settings.state
        var selectedTvProfile = ""
        var deletedTvProfile = ""
        val stored = mutableMapOf<String, ProfileNetworkSettings>()
        val publishedNetwork = mutableListOf<String>()

        init {
            coordinator.handleOpenBlank(7, 1)
            tabs.start(coordinator.snapshot())
        }

        val handler = BrowserRemoteCommandHandler(
            coordinator = coordinator,
            tabs = tabs,
            viewSettings = settings,
            applyTabs = { transition ->
                tabTransitions += 1
                transition.effects.filterIsInstance<TabEffect.Show>().lastOrNull()?.let { shown ->
                    coordinator.activatePage(tabs.pageFor(shown.id)!!)
                }
            },
            reload = {},
            setView = { next, _ -> view = next },
            exitFullscreen = {},
            startFind = {},
            findNext = {},
            findPrevious = {},
            clearFind = {},
            showNotice = {},
            publishTabs = {},
            publishView = {},
            selectTvProfile = { selectedTvProfile = it },
            selectDevice = {},
            createTvProfile = {},
            renameTvProfile = { _, _ -> },
            deleteTvProfile = { deletedTvProfile = it },
            publishProfilesAndLibrary = {},
            setNetwork = { profileId, settings ->
                stored[profileId] = settings
                true
            },
            clearNetwork = { profileId -> stored.remove(profileId) },
            publishNetwork = { profileId -> publishedNetwork += profileId },
            activeTvProfileId = { "family" },
            previousNetwork = { profileId -> stored[profileId] },
        )
    }
}
