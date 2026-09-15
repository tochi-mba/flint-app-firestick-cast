package com.rextechnologies.flint.protocol.wire

/** Strict payload codec for the additive browser messages 21 through 31. */
internal object BrowserWirePhase2Codec {
    fun encode(message: WireMessage): ByteArray {
        validate(message)
        val writer = PayloadWriter()
        when (message) {
            is BrowserTabCommandMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.commandId)
                writer.u8(message.action.id)
                writer.i64(message.tabId)
                writer.nullableUtf8U16(message.url, BrowserWireLimits.MAX_URL_BYTES)
            }

            is BrowserTabStateMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.revision)
                writer.i64(message.activeTabId)
                writer.u8(message.tabs.size)
                message.tabs.forEach { encodeTab(writer, it) }
            }

            is BrowserViewCommandMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.commandId)
                writer.u8(message.action.id)
                writer.i32(message.value)
                writer.utf8U16(message.text, viewTextLimit(message.action))
            }

            is BrowserViewStateMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.revision)
                writer.i32(message.zoomPercent)
                writer.u8(message.userAgentMode.id)
                writer.u8(message.darkMode.id)
                writer.u8(message.inputMode.id)
                writer.boolean(message.fullscreen)
                writer.boolean(message.mediaPlaying)
                writer.boolean(message.editingFocused)
                writer.boolean(message.findActive)
                writer.i32(message.findCurrent)
                writer.i32(message.findTotal)
                writer.u8(message.searchEngine.id)
            }

            is BrowserFaviconMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.faviconId)
                writer.u16(message.width)
                writer.u16(message.height)
                writer.binary(message.png, BrowserWireLimits.MAX_FAVICON_BYTES)
            }

            is BrowserLibraryCommandMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.commandId)
                writer.u8(message.action.id)
                writer.utf8U16(message.url, BrowserWireLimits.MAX_URL_BYTES)
                writer.utf8U16(message.title, BrowserWireLimits.MAX_TITLE_BYTES)
            }

            is BrowserLibraryStateMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.revision)
                writer.u8(message.bookmarks.size)
                writer.u8(message.history.size)
                message.bookmarks.forEach { encodeLibraryEntry(writer, it) }
                message.history.forEach { encodeLibraryEntry(writer, it) }
            }

            is BrowserProfileCommandMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.commandId)
                writer.u8(message.action.id)
                writer.utf8U16(message.profileId, BrowserWireLimits.MAX_PROFILE_ID_BYTES)
                writer.utf8U16(message.name, BrowserWireLimits.MAX_PROFILE_NAME_BYTES)
            }

            is BrowserProfileStateMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.revision)
                writer.u8(message.activeSource.id)
                writer.utf8U16(message.activeProfileId, BrowserWireLimits.MAX_PROFILE_ID_BYTES)
                writer.utf8U16(message.deviceName, BrowserWireLimits.MAX_DEVICE_PROFILE_NAME_BYTES)
                writer.u8(message.profiles.size)
                message.profiles.forEach { profile ->
                    writer.utf8U16(profile.profileId, BrowserWireLimits.MAX_PROFILE_ID_BYTES)
                    writer.utf8U16(profile.name, BrowserWireLimits.MAX_PROFILE_NAME_BYTES)
                }
            }

            is BrowserNetworkCommandMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.commandId)
                writer.u8(message.action.id)
                writer.utf8U16(message.profileId, BrowserWireLimits.MAX_PROFILE_ID_BYTES)
                writer.boolean(message.vpnEnabled)
                writer.u8(message.provider.id)
                writer.boolean(message.autoConnectOnBrowserStart)
                writer.boolean(message.requireVpnBeforeBrowse)
                writer.utf8U32(message.configText, BrowserWireLimits.MAX_CONFIG_UTF8_BYTES)
            }

            is BrowserNetworkStateMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.revision)
                writer.utf8U16(message.profileId, BrowserWireLimits.MAX_PROFILE_ID_BYTES)
                writer.boolean(message.vpnEnabled)
                writer.u8(message.provider.id)
                writer.boolean(message.autoConnectOnBrowserStart)
                writer.boolean(message.requireVpnBeforeBrowse)
                writer.boolean(message.configPresent)
                writer.boolean(message.capabilityPreparable)
                writer.utf8U16(message.capabilityReason, BrowserWireLimits.MAX_DETAIL_BYTES)
                writer.u8(message.sessionState.id)
                writer.utf8U16(message.sessionDetail, BrowserWireLimits.MAX_DETAIL_BYTES)
            }

            else -> throw WireFormatException("Unhandled phase-two browser message type: ${message::class.simpleName}")
        }
        return writer.toByteArray()
    }

    fun decode(type: WireMessageType, payload: ByteArray): WireMessage {
        val reader = PayloadReader(payload)
        val message = when (type) {
            WireMessageType.BROWSER_TAB_COMMAND -> BrowserTabCommandMessage(
                epoch = reader.i64("browser tab command epoch"),
                commandId = reader.i64("browser tab command ID"),
                action = BrowserTabAction.fromId(reader.u8("browser tab command action"))
                    ?: unknown("browser tab command action"),
                tabId = reader.i64("browser tab command tab ID"),
                url = reader.nullableUtf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser tab command URL"),
            )

            WireMessageType.BROWSER_TAB_STATE -> decodeTabState(reader)
            WireMessageType.BROWSER_VIEW_COMMAND -> decodeViewCommand(reader)
            WireMessageType.BROWSER_VIEW_STATE -> BrowserViewStateMessage(
                epoch = reader.i64("browser view state epoch"),
                revision = reader.i64("browser view state revision"),
                zoomPercent = reader.i32("browser zoom percent"),
                userAgentMode = BrowserUserAgentMode.fromId(reader.u8("browser user-agent mode"))
                    ?: unknown("browser user-agent mode"),
                darkMode = BrowserDarkMode.fromId(reader.u8("browser dark mode"))
                    ?: unknown("browser dark mode"),
                inputMode = BrowserInteractionMode.fromId(reader.u8("browser input mode"))
                    ?: unknown("browser input mode"),
                fullscreen = reader.boolean("browser fullscreen flag"),
                mediaPlaying = reader.boolean("browser media-playing flag"),
                editingFocused = reader.boolean("browser editing-focused flag"),
                findActive = reader.boolean("browser find-active flag"),
                findCurrent = reader.i32("browser find current"),
                findTotal = reader.i32("browser find total"),
                searchEngine = BrowserSearchEngine.fromId(reader.u8("browser search engine"))
                    ?: unknown("browser search engine"),
            )

            WireMessageType.BROWSER_FAVICON -> BrowserFaviconMessage(
                epoch = reader.i64("browser favicon epoch"),
                faviconId = reader.i64("browser favicon ID"),
                width = reader.u16("browser favicon width"),
                height = reader.u16("browser favicon height"),
                png = reader.binary(BrowserWireLimits.MAX_FAVICON_BYTES, "browser favicon PNG"),
            )

            WireMessageType.BROWSER_LIBRARY_COMMAND -> BrowserLibraryCommandMessage(
                epoch = reader.i64("browser library command epoch"),
                commandId = reader.i64("browser library command ID"),
                action = BrowserLibraryAction.fromId(reader.u8("browser library command action"))
                    ?: unknown("browser library command action"),
                url = reader.utf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser library command URL"),
                title = reader.utf8U16(BrowserWireLimits.MAX_TITLE_BYTES, "browser library command title"),
            )

            WireMessageType.BROWSER_LIBRARY_STATE -> decodeLibraryState(reader)
            WireMessageType.BROWSER_PROFILE_COMMAND -> BrowserProfileCommandMessage(
                epoch = reader.i64("browser profile command epoch"),
                commandId = reader.i64("browser profile command ID"),
                action = BrowserProfileAction.fromId(reader.u8("browser profile command action"))
                    ?: unknown("browser profile command action"),
                profileId = reader.utf8U16(
                    BrowserWireLimits.MAX_PROFILE_ID_BYTES,
                    "browser profile command profile ID",
                ),
                name = reader.utf8U16(
                    BrowserWireLimits.MAX_PROFILE_NAME_BYTES,
                    "browser profile command name",
                ),
            )
            WireMessageType.BROWSER_PROFILE_STATE -> decodeProfileState(reader)
            WireMessageType.BROWSER_NETWORK_COMMAND -> BrowserNetworkCommandMessage(
                epoch = reader.i64("browser network command epoch"),
                commandId = reader.i64("browser network command ID"),
                action = BrowserNetworkAction.fromId(reader.u8("browser network command action"))
                    ?: unknown("browser network command action"),
                profileId = reader.utf8U16(
                    BrowserWireLimits.MAX_PROFILE_ID_BYTES,
                    "browser network command profile ID",
                ),
                vpnEnabled = reader.boolean("browser network command vpn enabled"),
                provider = BrowserVpnProvider.fromId(reader.u8("browser network command provider"))
                    ?: unknown("browser network command provider"),
                autoConnectOnBrowserStart = reader.boolean("browser network command auto-connect"),
                requireVpnBeforeBrowse = reader.boolean("browser network command require VPN before browse"),
                configText = reader.utf8U32(
                    BrowserWireLimits.MAX_CONFIG_UTF8_BYTES,
                    "browser network command config",
                ),
            )
            WireMessageType.BROWSER_NETWORK_STATE -> BrowserNetworkStateMessage(
                epoch = reader.i64("browser network state epoch"),
                revision = reader.i64("browser network state revision"),
                profileId = reader.utf8U16(
                    BrowserWireLimits.MAX_PROFILE_ID_BYTES,
                    "browser network state profile ID",
                ),
                vpnEnabled = reader.boolean("browser network state vpn enabled"),
                provider = BrowserVpnProvider.fromId(reader.u8("browser network state provider"))
                    ?: unknown("browser network state provider"),
                autoConnectOnBrowserStart = reader.boolean("browser network state auto-connect"),
                requireVpnBeforeBrowse = reader.boolean("browser network state require VPN before browse"),
                configPresent = reader.boolean("browser network state config present"),
                capabilityPreparable = reader.boolean("browser network state capability preparable"),
                capabilityReason = reader.utf8U16(
                    BrowserWireLimits.MAX_DETAIL_BYTES,
                    "browser network capability reason",
                ),
                sessionState = BrowserVpnSessionState.fromId(reader.u8("browser network session state"))
                    ?: unknown("browser network session state"),
                sessionDetail = reader.utf8U16(
                    BrowserWireLimits.MAX_DETAIL_BYTES,
                    "browser network session detail",
                ),
            )
            else -> throw WireFormatException("Unhandled phase-two browser message type: ${type.id}")
        }
        validate(message)
        reader.requireFinished()
        return message
    }

    private fun decodeTabState(reader: PayloadReader): BrowserTabStateMessage {
        val epoch = reader.i64("browser tab state epoch")
        val revision = reader.i64("browser tab state revision")
        val activeTabId = reader.i64("browser active tab ID")
        val count = reader.u8("browser tab count")
        if (count > BrowserWireLimits.MAX_TABS) throw WireFormatException("Browser tab count is out of range: $count")
        return BrowserTabStateMessage(
            epoch,
            revision,
            activeTabId,
            List(count) { decodeTab(reader) },
        )
    }

    private fun decodeViewCommand(reader: PayloadReader): BrowserViewCommandMessage {
        val epoch = reader.i64("browser view command epoch")
        val commandId = reader.i64("browser view command ID")
        val action = BrowserViewAction.fromId(reader.u8("browser view command action"))
            ?: unknown("browser view command action")
        return BrowserViewCommandMessage(
            epoch,
            commandId,
            action,
            reader.i32("browser view command value"),
            reader.utf8U16(viewTextLimit(action), "browser view command text"),
        )
    }

    private fun decodeLibraryState(reader: PayloadReader): BrowserLibraryStateMessage {
        val epoch = reader.i64("browser library state epoch")
        val revision = reader.i64("browser library state revision")
        val bookmarkCount = reader.u8("browser bookmark count")
        val historyCount = reader.u8("browser history count")
        return BrowserLibraryStateMessage(
            epoch,
            revision,
            List(bookmarkCount) { decodeLibraryEntry(reader) },
            List(historyCount) { decodeLibraryEntry(reader) },
        )
    }

    private fun decodeProfileState(reader: PayloadReader): BrowserProfileStateMessage {
        val epoch = reader.i64("browser profile state epoch")
        val revision = reader.i64("browser profile state revision")
        val activeSource = BrowserProfileSource.fromId(reader.u8("browser active profile source"))
            ?: unknown("browser active profile source")
        val activeProfileId = reader.utf8U16(
            BrowserWireLimits.MAX_PROFILE_ID_BYTES,
            "browser active profile ID",
        )
        val deviceName = reader.utf8U16(
            BrowserWireLimits.MAX_DEVICE_PROFILE_NAME_BYTES,
            "browser profile device name",
        )
        val count = reader.u8("browser profile count")
        requireWire(count <= BrowserWireLimits.MAX_TV_PROFILES, "Browser profile count is out of range")
        return BrowserProfileStateMessage(
            epoch,
            revision,
            activeSource,
            activeProfileId,
            deviceName,
            List(count) {
                BrowserProfileEntry(
                    reader.utf8U16(BrowserWireLimits.MAX_PROFILE_ID_BYTES, "browser profile ID"),
                    reader.utf8U16(BrowserWireLimits.MAX_PROFILE_NAME_BYTES, "browser profile name"),
                )
            },
        )
    }

    private fun encodeTab(writer: PayloadWriter, tab: BrowserTabStateEntry) {
        writer.i64(tab.tabId)
        writer.u8(tab.loadState.id)
        writer.u8(tab.progress)
        writer.boolean(tab.canGoBack)
        writer.boolean(tab.canGoForward)
        writer.boolean(tab.frozen)
        writer.i64(tab.faviconId)
        writer.utf8U16(tab.url, BrowserWireLimits.MAX_URL_BYTES)
        writer.utf8U16(tab.title, BrowserWireLimits.MAX_TITLE_BYTES)
    }

    private fun decodeTab(reader: PayloadReader): BrowserTabStateEntry = BrowserTabStateEntry(
        tabId = reader.i64("browser tab ID"),
        loadState = BrowserLoadState.fromId(reader.u8("browser tab load state"))
            ?: unknown("browser tab load state"),
        progress = reader.u8("browser tab progress"),
        canGoBack = reader.boolean("browser tab can-go-back flag"),
        canGoForward = reader.boolean("browser tab can-go-forward flag"),
        frozen = reader.boolean("browser tab frozen flag"),
        faviconId = reader.i64("browser tab favicon ID"),
        url = reader.utf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser tab URL"),
        title = reader.utf8U16(BrowserWireLimits.MAX_TITLE_BYTES, "browser tab title"),
    )

    private fun encodeLibraryEntry(writer: PayloadWriter, entry: BrowserLibraryEntry) {
        writer.u8(entry.kind.id)
        writer.i64(entry.faviconId)
        writer.i64(entry.lastVisitedMs)
        writer.utf8U16(entry.url, BrowserWireLimits.MAX_URL_BYTES)
        writer.utf8U16(entry.title, BrowserWireLimits.MAX_TITLE_BYTES)
    }

    private fun decodeLibraryEntry(reader: PayloadReader): BrowserLibraryEntry = BrowserLibraryEntry(
        kind = BrowserLibraryEntryKind.fromId(reader.u8("browser library entry kind"))
            ?: unknown("browser library entry kind"),
        faviconId = reader.i64("browser library favicon ID"),
        lastVisitedMs = reader.i64("browser library last-visited time"),
        url = reader.utf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser library URL"),
        title = reader.utf8U16(BrowserWireLimits.MAX_TITLE_BYTES, "browser library title"),
    )

    private fun validate(message: WireMessage) {
        when (message) {
            is BrowserTabCommandMessage -> validate(message)
            is BrowserTabStateMessage -> validate(message)
            is BrowserViewCommandMessage -> validate(message)
            is BrowserViewStateMessage -> validate(message)
            is BrowserFaviconMessage -> validate(message)
            is BrowserLibraryCommandMessage -> validate(message)
            is BrowserLibraryStateMessage -> validate(message)
            is BrowserProfileCommandMessage -> validate(message)
            is BrowserProfileStateMessage -> validate(message)
            is BrowserNetworkCommandMessage -> validate(message)
            is BrowserNetworkStateMessage -> validate(message)
            else -> throw WireFormatException("Unhandled phase-two browser message type: ${message::class.simpleName}")
        }
    }

    private fun validate(message: BrowserTabCommandMessage) {
        requirePositive(message.epoch, "browser tab command epoch")
        requirePositive(message.commandId, "browser tab command ID")
        if (message.action == BrowserTabAction.NEW) {
            requireWire(message.tabId == 0L, "New browser tab command must use tab ID zero")
        } else {
            requirePositive(message.tabId, "browser tab command tab ID")
        }
        requireWire(
            message.action == BrowserTabAction.NEW || message.url == null,
            "Only new browser tab command carries a URL",
        )
        message.url?.let {
            requireWire(it.isNotBlank(), "Browser tab command URL is blank")
            requireTextLength(it, BrowserWireLimits.MAX_URL_BYTES, "Browser tab command URL")
        }
    }

    private fun validate(message: BrowserTabStateMessage) {
        requirePositive(message.epoch, "browser tab state epoch")
        requirePositive(message.revision, "browser tab state revision")
        requireWire(message.tabs.size <= BrowserWireLimits.MAX_TABS, "Browser tab count is out of range")
        requireWire(
            (message.tabs.isEmpty() && message.activeTabId == 0L) ||
                (message.tabs.isNotEmpty() && message.activeTabId > 0L),
            "Browser active tab ID does not match tab presence",
        )
        val ids = HashSet<Long>(message.tabs.size)
        message.tabs.forEach {
            validate(it)
            requireWire(ids.add(it.tabId), "Browser tab IDs must be unique")
        }
        if (message.tabs.isNotEmpty()) {
            requireWire(message.activeTabId in ids, "Browser active tab ID is not present")
        }
    }

    private fun validate(tab: BrowserTabStateEntry) {
        requirePositive(tab.tabId, "browser tab ID")
        requireNonNegative(tab.faviconId, "browser tab favicon ID")
        requireWire(tab.progress in 0..100, "Browser tab progress is out of range")
        requireTextLength(tab.url, BrowserWireLimits.MAX_URL_BYTES, "Browser tab URL")
        requireTextLength(tab.title, BrowserWireLimits.MAX_TITLE_BYTES, "Browser tab title")
        if (tab.loadState in setOf(BrowserLoadState.LOADING, BrowserLoadState.LOADED, BrowserLoadState.FAILED)) {
            requireWire(tab.url.isNotBlank(), "Active browser tab URL is blank")
        }
    }

    private fun validate(message: BrowserViewCommandMessage) {
        requirePositive(message.epoch, "browser view command epoch")
        requirePositive(message.commandId, "browser view command ID")
        requireTextLength(message.text, viewTextLimit(message.action), "Browser view command text")
        when (message.action) {
            BrowserViewAction.SET_ZOOM -> {
                requireWire(
                    message.value in BrowserWireLimits.MIN_ZOOM_PERCENT..BrowserWireLimits.MAX_ZOOM_PERCENT,
                    "Browser zoom percent is out of range",
                )
                requireNoText(message)
            }

            BrowserViewAction.SET_UA -> {
                requireEnumValue(message.value, BrowserUserAgentMode::fromId, "browser user-agent mode")
                requireNoText(message)
            }

            BrowserViewAction.SET_DARK -> {
                requireEnumValue(message.value, BrowserDarkMode::fromId, "browser dark mode")
                requireNoText(message)
            }

            BrowserViewAction.SET_INPUT_MODE -> {
                requireEnumValue(message.value, BrowserInteractionMode::fromId, "browser input mode")
                requireNoText(message)
            }

            BrowserViewAction.SET_FULLSCREEN -> {
                requireWire(message.value == 0 || message.value == 1, "Browser fullscreen value is not boolean")
                requireNoText(message)
            }

            BrowserViewAction.FIND_START -> {
                requireWire(message.value == 0, "Find-start command value must be zero")
                requireWire(message.text.isNotBlank(), "Browser find text is blank")
            }

            BrowserViewAction.FIND_NEXT,
            BrowserViewAction.FIND_PREV,
            BrowserViewAction.FIND_CLEAR,
            -> {
                requireWire(message.value == 0, "Browser find command value must be zero")
                requireNoText(message)
            }

            BrowserViewAction.SET_SEARCH_ENGINE -> {
                val engine = requireEnumValue(message.value, BrowserSearchEngine::fromId, "browser search engine")
                if (engine == BrowserSearchEngine.CUSTOM) {
                    requireWire(message.text.isNotBlank(), "Custom browser search template is blank")
                } else {
                    requireNoText(message)
                }
            }
        }
    }

    private fun validate(message: BrowserViewStateMessage) {
        requirePositive(message.epoch, "browser view state epoch")
        requirePositive(message.revision, "browser view state revision")
        requireWire(
            message.zoomPercent in BrowserWireLimits.MIN_ZOOM_PERCENT..BrowserWireLimits.MAX_ZOOM_PERCENT,
            "Browser zoom percent is out of range",
        )
        requireWire(message.findCurrent >= 0 && message.findTotal >= 0, "Browser find counters are negative")
        if (message.findActive) {
            requireWire(
                (message.findTotal == 0 && message.findCurrent == 0) ||
                    (message.findTotal > 0 && message.findCurrent in 1..message.findTotal),
                "Browser find counters are inconsistent",
            )
        } else {
            requireWire(message.findCurrent == 0 && message.findTotal == 0, "Inactive browser find carries counters")
        }
    }

    private fun validate(message: BrowserFaviconMessage) {
        requirePositive(message.epoch, "browser favicon epoch")
        requirePositive(message.faviconId, "browser favicon ID")
        requireWire(message.width in 1..BrowserWireLimits.MAX_FAVICON_DIMENSION,
            "Browser favicon width is out of range")
        requireWire(message.height in 1..BrowserWireLimits.MAX_FAVICON_DIMENSION,
            "Browser favicon height is out of range")
        requireWire(message.png.size in 1..BrowserWireLimits.MAX_FAVICON_BYTES,
            "Browser favicon PNG length is out of range")
    }

    private fun validate(message: BrowserLibraryCommandMessage) {
        requirePositive(message.epoch, "browser library command epoch")
        requirePositive(message.commandId, "browser library command ID")
        requireTextLength(message.url, BrowserWireLimits.MAX_URL_BYTES, "Browser library command URL")
        requireTextLength(message.title, BrowserWireLimits.MAX_TITLE_BYTES, "Browser library command title")
        when (message.action) {
            BrowserLibraryAction.ADD_BOOKMARK -> requireWire(message.url.isNotBlank(), "Add-bookmark URL is blank")
            BrowserLibraryAction.REMOVE_BOOKMARK -> {
                requireWire(message.url.isNotBlank(), "Remove-bookmark URL is blank")
                requireWire(message.title.isEmpty(), "Remove-bookmark command carries a title")
            }

            BrowserLibraryAction.CLEAR_HISTORY,
            BrowserLibraryAction.CLEAR_BOOKMARKS,
            BrowserLibraryAction.REQUEST_SNAPSHOT,
            -> requireWire(message.url.isEmpty() && message.title.isEmpty(),
                "Parameterless browser library command carries text")
        }
    }

    private fun validate(message: BrowserLibraryStateMessage) {
        requirePositive(message.epoch, "browser library state epoch")
        requirePositive(message.revision, "browser library state revision")
        requireWire(message.bookmarks.size <= BrowserWireLimits.MAX_BOOKMARKS,
            "Browser bookmark count is out of range")
        requireWire(message.history.size <= BrowserWireLimits.MAX_HISTORY_ENTRIES,
            "Browser history count is out of range")
        message.bookmarks.forEach { validate(it, BrowserLibraryEntryKind.BOOKMARK) }
        message.history.forEach { validate(it, BrowserLibraryEntryKind.HISTORY) }
    }

    private fun validate(message: BrowserProfileCommandMessage) {
        requirePositive(message.epoch, "browser profile command epoch")
        requirePositive(message.commandId, "browser profile command ID")
        requireTextLength(
            message.profileId,
            BrowserWireLimits.MAX_PROFILE_ID_BYTES,
            "Browser profile command profile ID",
        )
        requireTextLength(message.name, BrowserWireLimits.MAX_PROFILE_NAME_BYTES, "Browser profile command name")
        when (message.action) {
            BrowserProfileAction.SELECT_TV_PROFILE,
            BrowserProfileAction.DELETE_TV_PROFILE,
            -> {
                validateProfileId(message.profileId)
                requireWire(message.name.isEmpty(), "Browser profile command carries a name")
            }

            BrowserProfileAction.CREATE_TV_PROFILE -> {
                requireWire(message.profileId.isEmpty(), "Create-profile command carries a profile ID")
                validateProfileName(message.name)
            }

            BrowserProfileAction.RENAME_TV_PROFILE -> {
                validateProfileId(message.profileId)
                validateProfileName(message.name)
            }

            BrowserProfileAction.SELECT_DEVICE,
            BrowserProfileAction.REQUEST_SNAPSHOT,
            -> requireWire(
                message.profileId.isEmpty() && message.name.isEmpty(),
                "Parameterless browser profile command carries text",
            )
        }
    }

    private fun validate(message: BrowserProfileStateMessage) {
        requirePositive(message.epoch, "browser profile state epoch")
        requirePositive(message.revision, "browser profile state revision")
        requireTextLength(
            message.activeProfileId,
            BrowserWireLimits.MAX_PROFILE_ID_BYTES,
            "Browser active profile ID",
        )
        validatePrintable(
            message.deviceName,
            BrowserWireLimits.MAX_DEVICE_PROFILE_NAME_BYTES,
            "Browser profile device name",
        )
        requireWire(
            message.profiles.size <= BrowserWireLimits.MAX_TV_PROFILES,
            "Browser profile count is out of range",
        )
        val ids = HashSet<String>(message.profiles.size)
        val names = HashSet<String>(message.profiles.size)
        message.profiles.forEach { profile ->
            validateProfileId(profile.profileId)
            validateProfileName(profile.name)
            requireWire(ids.add(profile.profileId), "Browser profile IDs must be unique")
            requireWire(names.add(profile.name), "Browser profile names must be unique")
        }
        if (message.activeSource == BrowserProfileSource.TV) {
            validateProfileId(message.activeProfileId)
            requireWire(message.activeProfileId in ids, "Active TV browser profile is not present")
        } else {
            requireWire(message.activeProfileId.isEmpty(), "Device profile source carries a TV profile ID")
        }
    }

    private fun validate(message: BrowserNetworkCommandMessage) {
        requirePositive(message.epoch, "browser network command epoch")
        requirePositive(message.commandId, "browser network command ID")
        requireTextLength(
            message.profileId,
            BrowserWireLimits.MAX_PROFILE_ID_BYTES,
            "Browser network command profile ID",
        )
        requireTextLength(
            message.configText,
            BrowserWireLimits.MAX_CONFIG_UTF8_BYTES,
            "Browser network command config",
        )
        when (message.action) {
            BrowserNetworkAction.SET -> {
                validateProfileId(message.profileId)
                if (message.vpnEnabled) {
                    requireWire(
                        message.provider == BrowserVpnProvider.WIREGUARD,
                        "Enabled VPN requires the WireGuard provider",
                    )
                }
                if (message.configText.isNotEmpty()) {
                    requireWire(
                        WireGuardConfigValidator.isValid(message.configText),
                        "WireGuard config is incomplete",
                    )
                }
            }

            BrowserNetworkAction.CLEAR -> {
                validateProfileId(message.profileId)
                requireNetworkCommandFieldsEmpty(message)
            }

            BrowserNetworkAction.REQUEST_SNAPSHOT -> {
                if (message.profileId.isNotEmpty()) {
                    validateProfileId(message.profileId)
                }
                requireNetworkCommandFieldsEmpty(message)
            }
        }
    }

    private fun requireNetworkCommandFieldsEmpty(message: BrowserNetworkCommandMessage) {
        requireWire(!message.vpnEnabled, "Browser network command carries vpnEnabled")
        requireWire(
            message.provider == BrowserVpnProvider.NONE,
            "Browser network command carries a provider",
        )
        requireWire(!message.autoConnectOnBrowserStart, "Browser network command carries auto-connect")
        requireWire(!message.requireVpnBeforeBrowse, "Browser network command carries require VPN before browse")
        requireWire(message.configText.isEmpty(), "Browser network command carries config text")
    }

    private fun validate(message: BrowserNetworkStateMessage) {
        requirePositive(message.epoch, "browser network state epoch")
        requirePositive(message.revision, "browser network state revision")
        if (message.profileId.isNotEmpty()) {
            validateProfileId(message.profileId)
        }
        requireTextLength(
            message.capabilityReason,
            BrowserWireLimits.MAX_DETAIL_BYTES,
            "Browser network capability reason",
        )
        requireTextLength(
            message.sessionDetail,
            BrowserWireLimits.MAX_DETAIL_BYTES,
            "Browser network session detail",
        )
    }

    private fun validateProfileId(profileId: String) {
        requireWire(profileId.isNotEmpty(), "Browser profile ID is empty")
        requireTextLength(profileId, BrowserWireLimits.MAX_PROFILE_ID_BYTES, "Browser profile ID")
        requireWire(
            profileId.all {
                it in 'A'..'Z' || it in 'a'..'z' || it in '0'..'9' || it == '_' || it == '-'
            },
            "Browser profile ID contains unsafe characters",
        )
    }

    private fun validateProfileName(name: String) {
        requireWire(name.isNotBlank(), "Browser profile name is blank")
        requireWire(name == name.trim(), "Browser profile name is not trimmed")
        validatePrintable(name, BrowserWireLimits.MAX_PROFILE_NAME_BYTES, "Browser profile name")
    }

    private fun validatePrintable(value: String, maximumBytes: Int, field: String) {
        requireTextLength(value, maximumBytes, field)
        requireWire(value.none(Char::isISOControl), "$field contains control characters")
    }

    private fun validate(entry: BrowserLibraryEntry, expectedKind: BrowserLibraryEntryKind) {
        requireWire(entry.kind == expectedKind, "Browser library entry is in the wrong collection")
        requireNonNegative(entry.faviconId, "browser library favicon ID")
        requireNonNegative(entry.lastVisitedMs, "browser library last-visited time")
        requireWire(entry.url.isNotBlank(), "Browser library URL is blank")
        requireTextLength(entry.url, BrowserWireLimits.MAX_URL_BYTES, "Browser library URL")
        requireTextLength(entry.title, BrowserWireLimits.MAX_TITLE_BYTES, "Browser library title")
    }

    private fun requireNoText(message: BrowserViewCommandMessage) =
        requireWire(message.text.isEmpty(), "Browser view command carries text")

    private fun viewTextLimit(action: BrowserViewAction): Int =
        if (action == BrowserViewAction.FIND_START) BrowserWireLimits.MAX_FIND_TEXT_BYTES
        else BrowserWireLimits.MAX_URL_BYTES

    private fun <T> requireEnumValue(value: Int, fromId: (Int) -> T?, field: String): T =
        fromId(value) ?: throw WireFormatException("Unknown $field: $value")

    private fun requirePositive(value: Long, field: String) = requireWire(value > 0, "$field must be positive")

    private fun requireNonNegative(value: Long, field: String) = requireWire(value >= 0, "$field is negative")

    private fun requireTextLength(value: String, maximumBytes: Int, field: String) {
        val length = value.toByteArray(Charsets.UTF_8).size
        requireWire(length <= maximumBytes, "$field is too long: $length bytes")
    }

    private fun requireWire(condition: Boolean, message: String) {
        if (!condition) throw WireFormatException(message)
    }

    private fun unknown(field: String): Nothing = throw WireFormatException("Unknown $field")
}
