package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData

/** Additive browser vectors mirrored from the Rust-authoritative golden corpus. */
internal object GoldenBrowserPhase2Vectors {
    private const val SAMPLE_WIREGUARD_CONFIG =
        "[Interface]\nPrivateKey = AAA=\n\n[Peer]\nPublicKey = BBB=\n"

    val all: Map<String, WireFrame> = linkedMapOf(
        "browser-tab-command-new" to WireFrame(
            2,
            BrowserTabCommandMessage(2, 21, BrowserTabAction.NEW, 0, "https://example.test/new"),
        ),
        "browser-tab-command-close" to WireFrame(
            2,
            BrowserTabCommandMessage(2, 22, BrowserTabAction.CLOSE, 7),
        ),
        "browser-tab-command-select" to WireFrame(
            2,
            BrowserTabCommandMessage(2, 23, BrowserTabAction.SELECT, 8),
        ),
        "browser-tab-command-move" to WireFrame(
            2,
            BrowserTabCommandMessage(2, 24, BrowserTabAction.MOVE, 9),
        ),
        "browser-tab-command-duplicate" to WireFrame(
            2,
            BrowserTabCommandMessage(2, 25, BrowserTabAction.DUPLICATE, 10),
        ),
        "browser-tab-state-empty" to WireFrame(
            2,
            BrowserTabStateMessage(2, 30, 0, emptyList()),
        ),
        "browser-tab-state-populated" to WireFrame(
            2,
            BrowserTabStateMessage(
                2,
                31,
                7,
                listOf(
                    BrowserTabStateEntry(
                        7,
                        BrowserLoadState.LOADED,
                        100,
                        true,
                        false,
                        false,
                        41,
                        "https://example.test/a",
                        "Alpha",
                    ),
                    BrowserTabStateEntry(
                        8,
                        BrowserLoadState.LOADING,
                        37,
                        false,
                        true,
                        true,
                        0,
                        "https://example.test/b",
                        "Beta",
                    ),
                ),
            ),
        ),
        "browser-view-command-zoom" to viewCommand(40, BrowserViewAction.SET_ZOOM, 125),
        "browser-view-command-ua-tv" to viewCommand(
            41,
            BrowserViewAction.SET_UA,
            BrowserUserAgentMode.TV.id,
        ),
        "browser-view-command-ua-desktop" to viewCommand(
            42,
            BrowserViewAction.SET_UA,
            BrowserUserAgentMode.DESKTOP.id,
        ),
        "browser-view-command-ua-mobile" to viewCommand(
            43,
            BrowserViewAction.SET_UA,
            BrowserUserAgentMode.MOBILE.id,
        ),
        "browser-view-command-dark-follow-system" to viewCommand(
            44,
            BrowserViewAction.SET_DARK,
            BrowserDarkMode.FOLLOW_SYSTEM.id,
        ),
        "browser-view-command-dark-light" to viewCommand(
            45,
            BrowserViewAction.SET_DARK,
            BrowserDarkMode.LIGHT.id,
        ),
        "browser-view-command-dark-dark" to viewCommand(
            46,
            BrowserViewAction.SET_DARK,
            BrowserDarkMode.DARK.id,
        ),
        "browser-view-command-input-cursor" to viewCommand(
            47,
            BrowserViewAction.SET_INPUT_MODE,
            BrowserInteractionMode.CURSOR.id,
        ),
        "browser-view-command-input-focus" to viewCommand(
            48,
            BrowserViewAction.SET_INPUT_MODE,
            BrowserInteractionMode.FOCUS.id,
        ),
        "browser-view-command-fullscreen" to viewCommand(
            49,
            BrowserViewAction.SET_FULLSCREEN,
            1,
        ),
        "browser-view-command-find-start" to viewCommand(
            50,
            BrowserViewAction.FIND_START,
            text = "needle",
        ),
        "browser-view-command-find-next" to viewCommand(51, BrowserViewAction.FIND_NEXT),
        "browser-view-command-find-prev" to viewCommand(52, BrowserViewAction.FIND_PREV),
        "browser-view-command-find-clear" to viewCommand(53, BrowserViewAction.FIND_CLEAR),
        "browser-view-command-search-duckduckgo" to viewCommand(
            54,
            BrowserViewAction.SET_SEARCH_ENGINE,
            BrowserSearchEngine.DUCKDUCKGO.id,
        ),
        "browser-view-command-search-google" to viewCommand(
            55,
            BrowserViewAction.SET_SEARCH_ENGINE,
            BrowserSearchEngine.GOOGLE.id,
        ),
        "browser-view-command-search-bing" to viewCommand(
            56,
            BrowserViewAction.SET_SEARCH_ENGINE,
            BrowserSearchEngine.BING.id,
        ),
        "browser-view-command-search-custom" to viewCommand(
            57,
            BrowserViewAction.SET_SEARCH_ENGINE,
            BrowserSearchEngine.CUSTOM.id,
            "https://search.example/?q={q}",
        ),
        "browser-view-state-active" to WireFrame(
            2,
            BrowserViewStateMessage(
                2,
                60,
                125,
                BrowserUserAgentMode.DESKTOP,
                BrowserDarkMode.DARK,
                BrowserInteractionMode.FOCUS,
                true,
                true,
                true,
                true,
                2,
                5,
                BrowserSearchEngine.BING,
            ),
        ),
        "browser-favicon-png" to WireFrame(
            2,
            BrowserFaviconMessage(
                2,
                61,
                2,
                2,
                BinaryData.of(byteArrayOf(0x89.toByte(), 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)),
            ),
        ),
        "browser-library-command-add" to WireFrame(
            2,
            BrowserLibraryCommandMessage(
                2,
                70,
                BrowserLibraryAction.ADD_BOOKMARK,
                "https://example.test/bookmark",
                "Example",
            ),
        ),
        "browser-library-command-remove" to WireFrame(
            2,
            BrowserLibraryCommandMessage(
                2,
                71,
                BrowserLibraryAction.REMOVE_BOOKMARK,
                "https://example.test/bookmark",
            ),
        ),
        "browser-library-command-clear-history" to WireFrame(
            2,
            BrowserLibraryCommandMessage(2, 72, BrowserLibraryAction.CLEAR_HISTORY),
        ),
        "browser-library-command-clear-bookmarks" to WireFrame(
            2,
            BrowserLibraryCommandMessage(2, 73, BrowserLibraryAction.CLEAR_BOOKMARKS),
        ),
        "browser-library-command-request-snapshot" to WireFrame(
            2,
            BrowserLibraryCommandMessage(2, 74, BrowserLibraryAction.REQUEST_SNAPSHOT),
        ),
        "browser-library-state-populated" to WireFrame(
            2,
            BrowserLibraryStateMessage(
                2,
                80,
                listOf(
                    BrowserLibraryEntry(
                        BrowserLibraryEntryKind.BOOKMARK,
                        61,
                        1_700_000_000_000,
                        "https://example.test/bookmark",
                        "Example",
                    ),
                ),
                listOf(
                    BrowserLibraryEntry(
                        BrowserLibraryEntryKind.HISTORY,
                        0,
                        1_700_000_001_234,
                        "https://example.test/recent",
                        "Recent",
                    ),
                ),
            ),
        ),
        "browser-library-state-empty" to WireFrame(
            2,
            BrowserLibraryStateMessage(2, 81, emptyList(), emptyList()),
        ),
        "browser-profile-command-select-tv" to profileCommand(
            90,
            BrowserProfileAction.SELECT_TV_PROFILE,
            "family",
        ),
        "browser-profile-command-create-tv" to profileCommand(
            91,
            BrowserProfileAction.CREATE_TV_PROFILE,
            name = "Kids 🚀",
        ),
        "browser-profile-command-rename-tv" to profileCommand(
            92,
            BrowserProfileAction.RENAME_TV_PROFILE,
            "kids_2",
            "Children",
        ),
        "browser-profile-command-delete-tv" to profileCommand(
            93,
            BrowserProfileAction.DELETE_TV_PROFILE,
            "kids_2",
        ),
        "browser-profile-command-select-device" to profileCommand(
            94,
            BrowserProfileAction.SELECT_DEVICE,
        ),
        "browser-profile-command-request-snapshot" to profileCommand(
            95,
            BrowserProfileAction.REQUEST_SNAPSHOT,
        ),
        "browser-profile-state-tv" to WireFrame(
            2,
            BrowserProfileStateMessage(
                3,
                100,
                BrowserProfileSource.TV,
                "family",
                "Tochukwu's PC",
                listOf(
                    BrowserProfileEntry("family", "Family"),
                    BrowserProfileEntry("kids_2", "Kids 🚀"),
                ),
            ),
        ),
        "browser-profile-state-device" to WireFrame(
            2,
            BrowserProfileStateMessage(
                3,
                101,
                BrowserProfileSource.DEVICE,
                "",
                "Tochukwu's PC",
                listOf(BrowserProfileEntry("family", "Family")),
            ),
        ),
        "browser-network-command-set" to WireFrame(
            2,
            BrowserNetworkCommandMessage(
                3,
                110,
                BrowserNetworkAction.SET,
                "family",
                true,
                BrowserVpnProvider.WIREGUARD,
                true,
                requireVpnBeforeBrowse = false,
                SAMPLE_WIREGUARD_CONFIG,
            ),
        ),
        "browser-network-command-clear" to networkCommand(
            111,
            BrowserNetworkAction.CLEAR,
            "family",
        ),
        "browser-network-command-request-snapshot" to networkCommand(
            112,
            BrowserNetworkAction.REQUEST_SNAPSHOT,
        ),
        "browser-network-state-connected" to WireFrame(
            2,
            BrowserNetworkStateMessage(
                3,
                120,
                "family",
                true,
                BrowserVpnProvider.WIREGUARD,
                true,
                requireVpnBeforeBrowse = false,
                true,
                true,
                "VpnService available",
                BrowserVpnSessionState.CONNECTED,
            ),
        ),
        "browser-network-state-idle" to WireFrame(
            2,
            BrowserNetworkStateMessage(
                3,
                121,
                "family",
                false,
                BrowserVpnProvider.NONE,
                false,
                requireVpnBeforeBrowse = false,
                false,
                false,
                "VPN probe not configured",
                BrowserVpnSessionState.IDLE,
            ),
        ),
    )

    private fun viewCommand(
        commandId: Long,
        action: BrowserViewAction,
        value: Int = 0,
        text: String = "",
    ): WireFrame = WireFrame(2, BrowserViewCommandMessage(2, commandId, action, value, text))

    private fun profileCommand(
        commandId: Long,
        action: BrowserProfileAction,
        profileId: String = "",
        name: String = "",
    ): WireFrame = WireFrame(
        2,
        BrowserProfileCommandMessage(3, commandId, action, profileId, name),
    )

    private fun networkCommand(
        commandId: Long,
        action: BrowserNetworkAction,
        profileId: String = "",
    ): WireFrame = WireFrame(
        2,
        BrowserNetworkCommandMessage(3, commandId, action, profileId),
    )
}
