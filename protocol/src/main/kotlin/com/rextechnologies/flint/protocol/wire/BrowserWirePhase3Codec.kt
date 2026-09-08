package com.rextechnologies.flint.protocol.wire

/** Strict payload codec for additive browser workspace messages 32 through 34. */
internal object BrowserWirePhase3Codec {
    fun encode(message: WireMessage): ByteArray {
        validate(message)
        val writer = PayloadWriter()
        when (message) {
            is BrowserWorkspaceResizeMessage -> {
                writer.i64(message.epoch); writer.i64(message.commandId); writer.i64(message.expectedRevision)
                writer.u16(message.column); writer.u16(message.row); writer.u8(message.mode)
            }
            is BrowserWorkspaceGeometryMessage -> {
                writer.i64(message.epoch); writer.i64(message.revision)
                writer.u16(message.column); writer.u16(message.row); writer.u8(message.mode)
            }
            is BrowserWorkspaceCommandMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.commandId)
                writer.i64(message.expectedRevision)
                writer.u8(message.action.id)
                writer.i64(message.paneId)
                writer.u8(message.value)
                writer.utf8U16(message.url, BrowserWireLimits.MAX_URL_BYTES)
            }

            is BrowserWorkspaceStateMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.revision)
                writer.u8(message.layout.id)
                writer.i64(message.focusedPaneId)
                writer.u8(message.interactionMode.id)
                writer.i64(message.pageFullscreenPaneId)
                writer.i64(message.theaterPaneId)
                writer.u8(message.maxLiveRenderers)
                writer.u8(message.maxOpenPanes)
                writer.u8(message.panes.size)
                message.panes.forEach { encodePane(writer, it) }
            }

            is BrowserWorkspaceInputMessage -> {
                writer.i64(message.epoch)
                writer.i64(message.commandId)
                writer.i64(message.expectedRevision)
                writer.i64(message.paneId)
                writer.u8(message.kind.id)
                when (message.kind) {
                    BrowserWorkspaceInputKind.KEY ->
                        writer.u8(message.key?.id ?: unknown("browser workspace input key"))
                    BrowserWorkspaceInputKind.TEXT ->
                        writer.utf8U16(message.text, BrowserWireLimits.MAX_TEXT_BYTES)
                }
            }

            else -> throw WireFormatException(
                "Unhandled phase-three browser message type: ${message::class.simpleName}",
            )
        }
        return writer.toByteArray()
    }

    fun decode(type: WireMessageType, payload: ByteArray): WireMessage {
        val reader = PayloadReader(payload)
        val message = when (type) {
            WireMessageType.BROWSER_WORKSPACE_RESIZE -> BrowserWorkspaceResizeMessage(
                reader.i64("epoch"), reader.i64("command ID"), reader.i64("revision"),
                reader.u16("column split"), reader.u16("row split"), reader.u8("mode"),
            )
            WireMessageType.BROWSER_WORKSPACE_GEOMETRY -> BrowserWorkspaceGeometryMessage(
                reader.i64("epoch"), reader.i64("revision"), reader.u16("column split"), reader.u16("row split"), reader.u8("mode"),
            )
            WireMessageType.BROWSER_WORKSPACE_COMMAND -> BrowserWorkspaceCommandMessage(
                epoch = reader.i64("browser workspace command epoch"),
                commandId = reader.i64("browser workspace command ID"),
                expectedRevision = reader.i64("browser workspace command expected revision"),
                action = BrowserWorkspaceCommandAction.fromId(reader.u8("browser workspace command action"))
                    ?: unknown("browser workspace command action"),
                paneId = reader.i64("browser workspace command pane ID"),
                value = reader.u8("browser workspace command value"),
                url = reader.utf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser workspace command URL"),
            )

            WireMessageType.BROWSER_WORKSPACE_STATE -> decodeState(reader)
            WireMessageType.BROWSER_WORKSPACE_INPUT -> decodeInput(reader)
            else -> throw WireFormatException("Unhandled phase-three browser message type: ${type.id}")
        }
        validate(message)
        reader.requireFinished()
        return message
    }

    private fun decodeState(reader: PayloadReader): BrowserWorkspaceStateMessage {
        val epoch = reader.i64("browser workspace state epoch")
        val revision = reader.i64("browser workspace state revision")
        val layout = BrowserWorkspaceWireLayout.fromId(reader.u8("browser workspace layout"))
            ?: unknown("browser workspace layout")
        val focusedPaneId = reader.i64("browser workspace focused pane ID")
        val interactionMode = BrowserWorkspaceWireInteractionMode.fromId(
            reader.u8("browser workspace interaction mode"),
        ) ?: unknown("browser workspace interaction mode")
        val pageFullscreenPaneId = reader.i64("browser workspace page-fullscreen pane ID")
        val theaterPaneId = reader.i64("browser workspace theater pane ID")
        val maxLiveRenderers = reader.u8("browser workspace max live renderers")
        val maxOpenPanes = reader.u8("browser workspace max open panes")
        val count = reader.u8("browser workspace pane count")
        if (count > BrowserWireLimits.MAX_WORKSPACE_PANES) {
            throw WireFormatException("Browser workspace pane count is out of range: $count")
        }
        return BrowserWorkspaceStateMessage(
            epoch,
            revision,
            layout,
            focusedPaneId,
            interactionMode,
            pageFullscreenPaneId,
            theaterPaneId,
            maxLiveRenderers,
            maxOpenPanes,
            List(count) { decodePane(reader) },
        )
    }

    private fun decodeInput(reader: PayloadReader): BrowserWorkspaceInputMessage {
        val epoch = reader.i64("browser workspace input epoch")
        val commandId = reader.i64("browser workspace input command ID")
        val expectedRevision = reader.i64("browser workspace input expected revision")
        val paneId = reader.i64("browser workspace input pane ID")
        val kind = BrowserWorkspaceInputKind.fromId(reader.u8("browser workspace input kind"))
            ?: unknown("browser workspace input kind")
        return when (kind) {
            BrowserWorkspaceInputKind.KEY -> BrowserWorkspaceInputMessage(
                epoch,
                commandId,
                expectedRevision,
                paneId,
                kind,
                key = BrowserSemanticKey.fromId(reader.u8("browser workspace input key"))
                    ?: unknown("browser workspace input key"),
            )

            BrowserWorkspaceInputKind.TEXT -> BrowserWorkspaceInputMessage(
                epoch,
                commandId,
                expectedRevision,
                paneId,
                kind,
                text = reader.utf8U16(BrowserWireLimits.MAX_TEXT_BYTES, "browser workspace input text"),
            )
        }
    }

    private fun encodePane(writer: PayloadWriter, pane: BrowserWorkspacePaneStateEntry) {
        writer.i64(pane.paneId)
        writer.u8(pane.slot)
        writer.u8(pane.residency.id)
        writer.utf8U16(pane.url, BrowserWireLimits.MAX_URL_BYTES)
        writer.utf8U16(pane.title, BrowserWireLimits.MAX_TITLE_BYTES)
        writer.boolean(pane.loading)
        writer.u8(pane.progress)
        writer.boolean(pane.canGoBack)
        writer.boolean(pane.canGoForward)
        writer.boolean(pane.desiredMuted)
        writer.u8(pane.muteApplication.id)
        writer.u8(pane.observedPlayback.id)
    }

    private fun decodePane(reader: PayloadReader): BrowserWorkspacePaneStateEntry = BrowserWorkspacePaneStateEntry(
        paneId = reader.i64("browser workspace pane ID"),
        slot = reader.u8("browser workspace pane slot"),
        residency = BrowserWorkspaceWirePaneResidency.fromId(reader.u8("browser workspace pane residency"))
            ?: unknown("browser workspace pane residency"),
        url = reader.utf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser workspace pane URL"),
        title = reader.utf8U16(BrowserWireLimits.MAX_TITLE_BYTES, "browser workspace pane title"),
        loading = reader.boolean("browser workspace pane loading flag"),
        progress = reader.u8("browser workspace pane progress"),
        canGoBack = reader.boolean("browser workspace pane can-go-back flag"),
        canGoForward = reader.boolean("browser workspace pane can-go-forward flag"),
        desiredMuted = reader.boolean("browser workspace pane desired-muted flag"),
        muteApplication = BrowserWorkspaceWireMuteApplication.fromId(
            reader.u8("browser workspace pane mute application"),
        ) ?: unknown("browser workspace pane mute application"),
        observedPlayback = BrowserWorkspaceWireObservedPlayback.fromId(
            reader.u8("browser workspace pane observed playback"),
        ) ?: unknown("browser workspace pane observed playback"),
    )

    private fun validateGeometry(epoch: Long, revision: Long, column: Int, row: Int, mode: Int) {
        if (epoch <= 0 || revision <= 0 || column !in 1500..8500 || row !in 1500..8500 || mode !in 0..2)
            throw WireFormatException("Invalid workspace geometry")
    }

    private fun validate(message: WireMessage) {
        when (message) {
            is BrowserWorkspaceResizeMessage -> {
                validateGeometry(message.epoch, message.expectedRevision, message.column, message.row, message.mode)
                if (message.commandId <= 0) throw WireFormatException("Resize command ID must be positive")
            }
            is BrowserWorkspaceGeometryMessage -> validateGeometry(message.epoch, message.revision, message.column, message.row, message.mode)
            is BrowserWorkspaceCommandMessage -> validate(message)
            is BrowserWorkspaceStateMessage -> validate(message)
            is BrowserWorkspaceInputMessage -> validate(message)
            else -> throw WireFormatException(
                "Unhandled phase-three browser message type: ${message::class.simpleName}",
            )
        }
    }

    private fun validate(message: BrowserWorkspaceCommandMessage) {
        requirePositive(message.epoch, "browser workspace command epoch")
        requirePositive(message.commandId, "browser workspace command ID")
        requirePositive(message.expectedRevision, "browser workspace command expected revision")
        requireTextLength(message.url, BrowserWireLimits.MAX_URL_BYTES, "Browser workspace command URL")
        when (message.action) {
            BrowserWorkspaceCommandAction.FOCUS,
            BrowserWorkspaceCommandAction.CLOSE_PANE,
            BrowserWorkspaceCommandAction.RELOAD,
            BrowserWorkspaceCommandAction.BACK,
            BrowserWorkspaceCommandAction.FORWARD,
            BrowserWorkspaceCommandAction.PLAY_PAUSE,
            BrowserWorkspaceCommandAction.ENTER_THEATER,
            -> {
                requirePositive(message.paneId, "browser workspace command pane ID")
                requireWire(message.value == 0, "Pane-targeted browser workspace command carries a value")
                requireWire(message.url.isEmpty(), "Pane-targeted browser workspace command carries a URL")
            }

            BrowserWorkspaceCommandAction.OPEN_PANE -> {
                requireWire(message.paneId == 0L, "Open-pane command carries a pane ID")
                requireWire(message.value == 0, "Open-pane command carries a value")
            }

            BrowserWorkspaceCommandAction.SET_LAYOUT -> {
                requireWire(message.paneId == 0L, "Set-layout command carries a pane ID")
                requireEnumValue(message.value, BrowserWorkspaceWireLayout::fromId, "browser workspace layout")
                requireWire(message.url.isEmpty(), "Set-layout command carries a URL")
            }

            BrowserWorkspaceCommandAction.NAVIGATE -> {
                requirePositive(message.paneId, "browser workspace command pane ID")
                requireWire(message.value == 0, "Navigate command carries a value")
                requireWire(message.url.isNotBlank(), "Browser workspace navigate URL is blank")
            }

            BrowserWorkspaceCommandAction.SET_MUTE -> {
                requirePositive(message.paneId, "browser workspace command pane ID")
                requireWire(message.value == 0 || message.value == 1, "Browser workspace mute value is not boolean")
                requireWire(message.url.isEmpty(), "Set-mute command carries a URL")
            }

            BrowserWorkspaceCommandAction.SET_INTERACTION -> {
                requireWire(message.paneId == 0L, "Set-interaction command carries a pane ID")
                requireEnumValue(
                    message.value,
                    BrowserWorkspaceWireInteractionMode::fromId,
                    "browser workspace interaction mode",
                )
                requireWire(message.url.isEmpty(), "Set-interaction command carries a URL")
            }

            BrowserWorkspaceCommandAction.EXIT_THEATER,
            BrowserWorkspaceCommandAction.REQUEST_SNAPSHOT,
            -> {
                requireWire(message.paneId == 0L, "Parameterless browser workspace command carries a pane ID")
                requireWire(message.value == 0, "Parameterless browser workspace command carries a value")
                requireWire(message.url.isEmpty(), "Parameterless browser workspace command carries a URL")
            }

            BrowserWorkspaceCommandAction.MOVE_PANE -> {
                requirePositive(message.paneId, "browser workspace command pane ID")
                requireWire(message.value <= 3, "Move-pane command slot is out of range")
                requireWire(message.url.isEmpty(), "Move-pane command carries a URL")
            }
        }
    }

    private fun validate(message: BrowserWorkspaceStateMessage) {
        requirePositive(message.epoch, "browser workspace state epoch")
        requirePositive(message.revision, "browser workspace state revision")
        requireWire(message.panes.size <= BrowserWireLimits.MAX_WORKSPACE_PANES,
            "Browser workspace pane count is out of range")
        requireWire(message.maxLiveRenderers in 1..message.maxOpenPanes,
            "Browser workspace max live renderers exceeds max open panes")
        val ids = HashSet<Long>(message.panes.size)
        message.panes.forEach {
            validate(it)
            requireWire(ids.add(it.paneId), "Browser workspace pane IDs must be unique")
        }
        if (message.panes.isEmpty()) {
            requireWire(message.focusedPaneId == 0L, "Empty browser workspace carries a focused pane ID")
            requireWire(message.pageFullscreenPaneId == 0L, "Empty browser workspace carries page fullscreen")
            requireWire(message.theaterPaneId == 0L, "Empty browser workspace carries theatre mode")
        } else {
            if (message.focusedPaneId > 0L) {
                requireWire(message.focusedPaneId in ids, "Browser workspace focused pane is not present")
            }
            if (message.pageFullscreenPaneId > 0L) {
                requireWire(message.pageFullscreenPaneId in ids,
                    "Browser workspace page-fullscreen pane is not present")
            }
            if (message.theaterPaneId > 0L) {
                requireWire(message.theaterPaneId in ids, "Browser workspace theatre pane is not present")
            }
        }
    }

    private fun validate(pane: BrowserWorkspacePaneStateEntry) {
        requirePositive(pane.paneId, "browser workspace pane ID")
        requireWire(pane.progress in 0..100, "Browser workspace pane progress is out of range")
        requireTextLength(pane.url, BrowserWireLimits.MAX_URL_BYTES, "Browser workspace pane URL")
        requireTextLength(pane.title, BrowserWireLimits.MAX_TITLE_BYTES, "Browser workspace pane title")
        if (pane.residency == BrowserWorkspaceWirePaneResidency.LIVE ||
            pane.residency == BrowserWorkspaceWirePaneResidency.FAILED
        ) {
            requireWire(pane.url.isNotBlank(), "Active browser workspace pane URL is blank")
        }
    }

    private fun validate(message: BrowserWorkspaceInputMessage) {
        requirePositive(message.epoch, "browser workspace input epoch")
        requirePositive(message.commandId, "browser workspace input command ID")
        requirePositive(message.expectedRevision, "browser workspace input expected revision")
        requirePositive(message.paneId, "browser workspace input pane ID")
        when (message.kind) {
            BrowserWorkspaceInputKind.KEY -> {
                requireWire(message.key != null, "Browser workspace key input is missing a key")
                requireWire(message.text.isEmpty(), "Browser workspace key input carries text")
            }

            BrowserWorkspaceInputKind.TEXT -> {
                requireWire(message.key == null, "Browser workspace text input carries a key")
                requireWire(message.text.isNotBlank(), "Browser workspace input text is blank")
                requireTextLength(message.text, BrowserWireLimits.MAX_TEXT_BYTES, "Browser workspace input text")
            }
        }
    }

    private fun <T> requireEnumValue(value: Int, fromId: (Int) -> T?, field: String): T =
        fromId(value) ?: throw WireFormatException("Unknown $field: $value")

    private fun requirePositive(value: Long, field: String) = requireWire(value > 0, "$field must be positive")

    private fun requireTextLength(value: String, maximumBytes: Int, field: String) {
        val length = value.toByteArray(Charsets.UTF_8).size
        requireWire(length <= maximumBytes, "$field is too long: $length bytes")
    }

    private fun requireWire(condition: Boolean, message: String) {
        if (!condition) throw WireFormatException(message)
    }

    private fun unknown(field: String): Nothing = throw WireFormatException("Unknown $field")
}
