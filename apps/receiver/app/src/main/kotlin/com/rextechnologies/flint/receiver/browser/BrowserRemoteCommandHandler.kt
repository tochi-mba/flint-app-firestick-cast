package com.rextechnologies.flint.receiver.browser

import android.util.Log
import com.rextechnologies.flint.protocol.wire.BrowserDarkMode
import com.rextechnologies.flint.protocol.wire.BrowserNetworkAction
import com.rextechnologies.flint.protocol.wire.BrowserNetworkCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserProfileAction
import com.rextechnologies.flint.protocol.wire.BrowserProfileCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserTabAction
import com.rextechnologies.flint.protocol.wire.BrowserTabCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserViewAction
import com.rextechnologies.flint.protocol.wire.BrowserViewCommandMessage
import com.rextechnologies.flint.protocol.wire.BrowserInteractionMode as WireInteractionMode
import com.rextechnologies.flint.protocol.wire.BrowserSearchEngine as WireSearchEngine
import com.rextechnologies.flint.protocol.wire.BrowserUserAgentMode as WireUserAgentMode
import com.rextechnologies.flint.protocol.wire.BrowserVpnProvider as WireVpnProvider

/** Executes authenticated cockpit commands after accepting them on the shared command stream. */
class BrowserRemoteCommandHandler(
    private val coordinator: BrowserCoordinator,
    private val tabs: BrowserTabSession,
    private val viewSettings: BrowserViewSettings,
    private val applyTabs: (TabTransition) -> Unit,
    private val reload: () -> Unit,
    private val setView: (BrowserViewState, Boolean) -> Unit,
    private val exitFullscreen: () -> Unit,
    private val startFind: (String) -> Unit,
    private val findNext: () -> Unit,
    private val findPrevious: () -> Unit,
    private val clearFind: () -> Unit,
    private val showNotice: (String) -> Unit,
    private val publishTabs: () -> Unit,
    private val publishView: () -> Unit,
    private val selectTvProfile: (String) -> Unit,
    private val selectDevice: () -> Unit,
    private val createTvProfile: (String) -> Unit,
    private val renameTvProfile: (String, String) -> Unit,
    private val deleteTvProfile: (String) -> Unit,
    private val publishProfilesAndLibrary: () -> Unit,
    private val setNetwork: (String, ProfileNetworkSettings) -> Boolean,
    private val clearNetwork: (String) -> Unit,
    private val publishNetwork: (String) -> Unit,
    private val activeTvProfileId: () -> String,
    private val previousNetwork: (String) -> ProfileNetworkSettings?,
    private val urlPolicy: BrowserUrlPolicy = BrowserUrlPolicy(),
) {
    fun handle(command: BrowserTabCommandMessage) {
        if (!accept(command.epoch, command.commandId, "tab")) return
        val current = coordinator.snapshot()
        when (command.action) {
            BrowserTabAction.NEW -> {
                val address = command.url?.let { (urlPolicy.evaluate(it) as? BrowserUrlResult.Accepted)?.url }
                if (command.url != null && address == null) {
                    showNotice("That address could not be opened.")
                    return
                }
                applyTabs(tabs.openAccepted(current, address))
            }
            BrowserTabAction.CLOSE -> applyTabs(tabs.close(current, command.tabId))
            BrowserTabAction.SELECT -> applyTabs(tabs.select(current, command.tabId))
            BrowserTabAction.DUPLICATE -> applyTabs(tabs.duplicate(current, command.tabId))
            BrowserTabAction.MOVE -> {
                // V2 names MOVE but has no destination field, so changing order would be a guess.
                showNotice("Reordering tabs is not available on this receiver version.")
                publishTabs()
            }
        }
    }

    fun handle(command: BrowserViewCommandMessage) {
        if (!accept(command.epoch, command.commandId, "view")) return
        when (command.action) {
            BrowserViewAction.SET_ZOOM -> setView(viewSettings.setZoom(command.value), true)
            BrowserViewAction.SET_UA -> {
                val mode = when (WireUserAgentMode.fromId(command.value)) {
                    WireUserAgentMode.DESKTOP -> BrowserUserAgentMode.DESKTOP
                    WireUserAgentMode.MOBILE -> BrowserUserAgentMode.MOBILE
                    else -> BrowserUserAgentMode.TV
                }
                setView(viewSettings.setUserAgent(mode), true)
                reload()
            }
            BrowserViewAction.SET_DARK -> setView(
                viewSettings.setDarkMode(BrowserDarkMode.fromId(command.value) != BrowserDarkMode.LIGHT),
                true,
            )
            BrowserViewAction.SET_INPUT_MODE -> setView(
                viewSettings.setInputMode(
                    if (WireInteractionMode.fromId(command.value) == WireInteractionMode.FOCUS) {
                        BrowserInteractionMode.FOCUS
                    } else {
                        BrowserInteractionMode.CURSOR
                    },
                ),
                false,
            )
            BrowserViewAction.SET_FULLSCREEN -> {
                if (command.value == 0) {
                    exitFullscreen()
                } else {
                    showNotice("Use the page's video control to enter fullscreen.")
                }
                publishView()
            }
            BrowserViewAction.FIND_START -> startFind(command.text)
            BrowserViewAction.FIND_NEXT -> findNext()
            BrowserViewAction.FIND_PREV -> findPrevious()
            BrowserViewAction.FIND_CLEAR -> clearFind()
            BrowserViewAction.SET_SEARCH_ENGINE -> {
                val engine = when (WireSearchEngine.fromId(command.value)) {
                    WireSearchEngine.GOOGLE -> BrowserSearchEngine.GOOGLE
                    WireSearchEngine.BING -> BrowserSearchEngine.BING
                    WireSearchEngine.CUSTOM -> BrowserSearchEngine.custom(command.text)
                    else -> BrowserSearchEngine.DUCKDUCKGO
                }
                if (engine == null) {
                    showNotice("That search template is not a valid HTTPS template.")
                } else {
                    setView(viewSettings.setSearchEngine(engine), false)
                }
            }
        }
    }

    fun handle(command: BrowserProfileCommandMessage) {
        if (!accept(command.epoch, command.commandId, "profile")) return
        when (command.action) {
            BrowserProfileAction.SELECT_TV_PROFILE -> selectTvProfile(command.profileId)
            BrowserProfileAction.CREATE_TV_PROFILE -> createTvProfile(command.name)
            BrowserProfileAction.RENAME_TV_PROFILE -> renameTvProfile(command.profileId, command.name)
            BrowserProfileAction.DELETE_TV_PROFILE -> deleteTvProfile(command.profileId)
            BrowserProfileAction.SELECT_DEVICE -> selectDevice()
            BrowserProfileAction.REQUEST_SNAPSHOT -> publishProfilesAndLibrary()
        }
    }

    fun handle(command: BrowserNetworkCommandMessage) {
        if (!accept(command.epoch, command.commandId, "network")) return
        when (command.action) {
            BrowserNetworkAction.SET -> {
                val previous = previousNetwork(command.profileId)
                // Empty configText on Set keeps the previously stored blob (host may omit it).
                val configText = if (command.configText.isNotEmpty()) {
                    command.configText
                } else {
                    previous?.configText.orEmpty()
                }
                val settings = ProfileNetworkSettings(
                    vpnEnabled = command.vpnEnabled,
                    provider = when (command.provider) {
                        WireVpnProvider.WIREGUARD -> VpnProvider.WIREGUARD
                        WireVpnProvider.NONE -> VpnProvider.NONE
                    },
                    autoConnectOnBrowserStart = command.autoConnectOnBrowserStart,
                    requireVpnBeforeBrowse = command.requireVpnBeforeBrowse,
                    configText = configText,
                )
                if (!setNetwork(command.profileId, settings)) {
                    showNotice("Network settings could not be saved.")
                }
                publishNetwork(command.profileId)
            }
            BrowserNetworkAction.CLEAR -> {
                clearNetwork(command.profileId)
                publishNetwork(command.profileId)
            }
            BrowserNetworkAction.REQUEST_SNAPSHOT -> {
                val profileId = command.profileId.ifEmpty { activeTvProfileId() }
                publishNetwork(profileId)
            }
        }
    }

    fun accept(epoch: Long, commandId: Long, family: String): Boolean {
        val accepted = coordinator.handleControl(epoch, commandId) is BrowserCommandEffect.Control
        if (!accepted) {
            Log.w(TAG, "Browser $family command rejected at $commandId")
        } else {
            Log.i(TAG, "Browser $family command accepted at $commandId")
        }
        return accepted
    }

    private companion object {
        const val TAG = "FlintBrowser"
    }
}
