package com.rextechnologies.flint.protocol.wire

/** Strict v2 browser payload codec. Transport authentication is deliberately outside this layer. */
internal object BrowserWireCodec {
    private const val MAX_VIEWPORT_DIMENSION = 16_384

    fun encode(message: WireMessage): ByteArray {
        val writer = PayloadWriter()
        when (message) {
            is BrowserCapabilityMessage -> {
                validate(message)
                encodeCapability(writer, message)
            }

            is BrowserCommandMessage -> {
                validate(message)
                encodeCommand(writer, message)
            }

            is BrowserInputMessage -> {
                validate(message)
                encodeInput(writer, message)
            }

            is BrowserStateMessage -> {
                validate(message)
                encodeState(writer, message)
            }

            is BrowserPreviewMessage -> {
                validate(message)
                encodePreview(writer, message)
            }

            is BrowserDialogMessage -> {
                validate(message)
                encodeDialog(writer, message)
            }

            is BrowserDialogReplyMessage -> {
                validate(message)
                encodeDialogReply(writer, message)
            }

            else -> throw WireFormatException("Unhandled browser message type: ${message::class.simpleName}")
        }
        return writer.toByteArray()
    }

    fun decode(type: WireMessageType, payload: ByteArray): WireMessage {
        val reader = PayloadReader(payload)
        val message = when (type) {
            WireMessageType.BROWSER_CAPABILITY -> decodeCapability(reader)
            WireMessageType.BROWSER_COMMAND -> decodeCommand(reader)
            WireMessageType.BROWSER_INPUT -> decodeInput(reader)
            WireMessageType.BROWSER_STATE -> decodeState(reader)
            WireMessageType.BROWSER_PREVIEW -> decodePreview(reader)
            WireMessageType.BROWSER_DIALOG -> decodeDialog(reader)
            WireMessageType.BROWSER_DIALOG_REPLY -> decodeDialogReply(reader)
            else -> throw WireFormatException("Unhandled browser message type: ${type.id}")
        }
        reader.requireFinished()
        return message
    }

    private fun encodeCapability(writer: PayloadWriter, message: BrowserCapabilityMessage) {
        writer.u8(message.status.id)
        writer.u16(message.secureEndpointPort)
        writer.u16(message.apiLevel)
        writer.utf8U16(message.webViewVersion, BrowserWireLimits.MAX_TITLE_BYTES)
        writer.boolean(message.previewSupported)
        writer.u16(message.previewMaxWidth)
        writer.u16(message.previewMaxHeight)
        writer.u8(message.interactivePreviewFramesPerSecond)
        writer.u8(message.idlePreviewFramesPerSecond)
        writer.i32(message.previewMaxBytes)
        writer.utf8U16(message.detail, BrowserWireLimits.MAX_DETAIL_BYTES)
    }

    private fun decodeCapability(reader: PayloadReader): BrowserCapabilityMessage = BrowserCapabilityMessage(
        status = BrowserCapabilityStatus.fromId(reader.u8("browser capability status"))
            ?: unknown("browser capability status"),
        secureEndpointPort = reader.u16("browser secure endpoint port"),
        apiLevel = reader.u16("browser API level"),
        webViewVersion = reader.utf8U16(BrowserWireLimits.MAX_TITLE_BYTES, "WebView version"),
        previewSupported = reader.boolean("browser preview supported"),
        previewMaxWidth = reader.u16("browser preview maximum width"),
        previewMaxHeight = reader.u16("browser preview maximum height"),
        interactivePreviewFramesPerSecond = reader.u8("browser interactive preview rate"),
        idlePreviewFramesPerSecond = reader.u8("browser idle preview rate"),
        previewMaxBytes = reader.i32("browser preview maximum bytes"),
        detail = reader.utf8U16(BrowserWireLimits.MAX_DETAIL_BYTES, "browser capability detail"),
    ).also(::validate)

    private fun encodeCommand(writer: PayloadWriter, message: BrowserCommandMessage) {
        writer.i64(message.epoch)
        writer.i64(message.commandId)
        writer.u8(message.action.id)
        when (message.action) {
            BrowserCommandAction.OPEN,
            BrowserCommandAction.NAVIGATE,
            -> writer.utf8U16(message.url!!, BrowserWireLimits.MAX_URL_BYTES)

            BrowserCommandAction.SET_PREVIEW_ENABLED -> writer.boolean(message.previewEnabled!!)
            else -> Unit
        }
    }

    private fun decodeCommand(reader: PayloadReader): BrowserCommandMessage {
        val epoch = reader.i64("browser command epoch")
        val commandId = reader.i64("browser command ID")
        val action = BrowserCommandAction.fromId(reader.u8("browser command action"))
            ?: unknown("browser command action")
        return when (action) {
            BrowserCommandAction.OPEN,
            BrowserCommandAction.NAVIGATE,
            -> BrowserCommandMessage(
                epoch = epoch,
                commandId = commandId,
                action = action,
                url = reader.utf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser command URL"),
            )

            BrowserCommandAction.SET_PREVIEW_ENABLED -> BrowserCommandMessage(
                epoch = epoch,
                commandId = commandId,
                action = action,
                previewEnabled = reader.boolean("browser preview enabled"),
            )

            else -> BrowserCommandMessage(epoch, commandId, action)
        }.also(::validate)
    }

    private fun encodeInput(writer: PayloadWriter, message: BrowserInputMessage) {
        writer.i64(message.epoch)
        writer.i64(message.sequence)
        writer.u8(message.event.eventId)
        when (val event = message.event) {
            is BrowserPointerInput -> {
                writer.u8(event.action.id)
                writePreviewReference(writer, event.navigationId, event.frameId, event.x, event.y)
                writer.i32(event.buttons)
            }

            is BrowserScrollInput -> {
                writePreviewReference(writer, event.navigationId, event.frameId, event.x, event.y)
                writer.i32(event.deltaX)
                writer.i32(event.deltaY)
            }

            is BrowserSemanticKeyInput -> writer.u8(event.key.id)
            is BrowserTextInput -> writer.utf8U16(event.text, BrowserWireLimits.MAX_TEXT_BYTES)
        }
    }

    private fun decodeInput(reader: PayloadReader): BrowserInputMessage {
        val epoch = reader.i64("browser input epoch")
        val sequence = reader.i64("browser input sequence")
        val event = when (val eventId = reader.u8("browser input type")) {
            1 -> decodePointerInput(reader)
            2 -> decodeScrollInput(reader)
            3 -> BrowserSemanticKeyInput(
                BrowserSemanticKey.fromId(reader.u8("browser semantic key"))
                    ?: unknown("browser semantic key"),
            )

            4 -> BrowserTextInput(reader.utf8U16(BrowserWireLimits.MAX_TEXT_BYTES, "browser text input"))
            else -> throw WireFormatException("Unknown browser input type: $eventId")
        }
        return BrowserInputMessage(epoch, sequence, event).also(::validate)
    }

    private fun decodePointerInput(reader: PayloadReader): BrowserPointerInput {
        val action = BrowserPointerAction.fromId(reader.u8("browser pointer action"))
            ?: unknown("browser pointer action")
        val reference = readPreviewReference(reader)
        return BrowserPointerInput(
            action,
            reference.navigationId,
            reference.frameId,
            reference.x,
            reference.y,
            reader.i32("browser pointer buttons"),
        )
    }

    private fun decodeScrollInput(reader: PayloadReader): BrowserScrollInput {
        val reference = readPreviewReference(reader)
        return BrowserScrollInput(
            reference.navigationId,
            reference.frameId,
            reference.x,
            reference.y,
            reader.i32("browser scroll delta X"),
            reader.i32("browser scroll delta Y"),
        )
    }

    private fun writePreviewReference(
        writer: PayloadWriter,
        navigationId: Long,
        frameId: Long,
        x: Int,
        y: Int,
    ) {
        writer.i64(navigationId)
        writer.i64(frameId)
        writer.u16(x)
        writer.u16(y)
    }

    private fun readPreviewReference(reader: PayloadReader): BrowserPreviewReference = BrowserPreviewReference(
        reader.i64("browser input navigation ID"),
        reader.i64("browser input frame ID"),
        reader.u16("browser input X"),
        reader.u16("browser input Y"),
    )

    private fun encodeState(writer: PayloadWriter, message: BrowserStateMessage) {
        writer.i64(message.epoch)
        writer.i64(message.revision)
        writer.i64(message.navigationId)
        writer.i64(message.lastAcceptedCommandId)
        writer.i64(message.lastAcceptedInputSequence)
        writer.u8(message.loadState.id)
        writer.utf8U16(message.url, BrowserWireLimits.MAX_URL_BYTES)
        writer.utf8U16(message.title, BrowserWireLimits.MAX_TITLE_BYTES)
        writer.u8(message.progress)
        writer.boolean(message.canGoBack)
        writer.boolean(message.canGoForward)
        writer.i32(message.viewportWidth)
        writer.i32(message.viewportHeight)
        writer.u8(message.previewState.id)
        writer.utf8U16(message.errorDetail, BrowserWireLimits.MAX_DETAIL_BYTES)
    }

    private fun decodeState(reader: PayloadReader): BrowserStateMessage = BrowserStateMessage(
        epoch = reader.i64("browser state epoch"),
        revision = reader.i64("browser state revision"),
        navigationId = reader.i64("browser state navigation ID"),
        lastAcceptedCommandId = reader.i64("browser state last accepted command ID"),
        lastAcceptedInputSequence = reader.i64("browser state last accepted input sequence"),
        loadState = BrowserLoadState.fromId(reader.u8("browser load state"))
            ?: unknown("browser load state"),
        url = reader.utf8U16(BrowserWireLimits.MAX_URL_BYTES, "browser state URL"),
        title = reader.utf8U16(BrowserWireLimits.MAX_TITLE_BYTES, "browser state title"),
        progress = reader.u8("browser state progress"),
        canGoBack = reader.boolean("browser state can go back"),
        canGoForward = reader.boolean("browser state can go forward"),
        viewportWidth = reader.i32("browser viewport width"),
        viewportHeight = reader.i32("browser viewport height"),
        previewState = BrowserPreviewState.fromId(reader.u8("browser preview state"))
            ?: unknown("browser preview state"),
        errorDetail = reader.utf8U16(BrowserWireLimits.MAX_DETAIL_BYTES, "browser state error detail"),
    ).also(::validate)

    private fun encodePreview(writer: PayloadWriter, message: BrowserPreviewMessage) {
        writer.i64(message.epoch)
        writer.i64(message.navigationId)
        writer.i64(message.frameId)
        writer.u16(message.width)
        writer.u16(message.height)
        writer.binary(message.jpeg, BrowserWireLimits.MAX_PREVIEW_BYTES)
    }

    private fun decodePreview(reader: PayloadReader): BrowserPreviewMessage = BrowserPreviewMessage(
        epoch = reader.i64("browser preview epoch"),
        navigationId = reader.i64("browser preview navigation ID"),
        frameId = reader.i64("browser preview frame ID"),
        width = reader.u16("browser preview width"),
        height = reader.u16("browser preview height"),
        jpeg = reader.binary(BrowserWireLimits.MAX_PREVIEW_BYTES, "browser preview JPEG"),
    ).also(::validate)

    private fun encodeDialog(writer: PayloadWriter, message: BrowserDialogMessage) {
        writer.i64(message.epoch)
        writer.i64(message.dialogId)
        writer.u8(message.type.id)
        writer.utf8U16(message.origin, BrowserWireLimits.MAX_ORIGIN_BYTES)
        writer.utf8U16(message.message, BrowserWireLimits.MAX_DIALOG_BYTES)
        writer.utf8U16(message.defaultValue, BrowserWireLimits.MAX_DIALOG_BYTES)
        writer.i32(message.timeoutMilliseconds)
    }

    private fun decodeDialog(reader: PayloadReader): BrowserDialogMessage = BrowserDialogMessage(
        epoch = reader.i64("browser dialog epoch"),
        dialogId = reader.i64("browser dialog ID"),
        type = BrowserDialogType.fromId(reader.u8("browser dialog type"))
            ?: unknown("browser dialog type"),
        origin = reader.utf8U16(BrowserWireLimits.MAX_ORIGIN_BYTES, "browser dialog origin"),
        message = reader.utf8U16(BrowserWireLimits.MAX_DIALOG_BYTES, "browser dialog message"),
        defaultValue = reader.utf8U16(BrowserWireLimits.MAX_DIALOG_BYTES, "browser dialog default value"),
        timeoutMilliseconds = reader.i32("browser dialog timeout"),
    ).also(::validate)

    private fun encodeDialogReply(writer: PayloadWriter, message: BrowserDialogReplyMessage) {
        writer.i64(message.epoch)
        writer.i64(message.dialogId)
        writer.boolean(message.accepted)
        writer.nullableUtf8U16(message.promptText, BrowserWireLimits.MAX_TEXT_BYTES)
    }

    private fun decodeDialogReply(reader: PayloadReader): BrowserDialogReplyMessage = BrowserDialogReplyMessage(
        epoch = reader.i64("browser dialog reply epoch"),
        dialogId = reader.i64("browser dialog reply ID"),
        accepted = reader.boolean("browser dialog accepted"),
        promptText = reader.nullableUtf8U16(BrowserWireLimits.MAX_TEXT_BYTES, "browser dialog prompt text"),
    ).also(::validate)

    private fun validate(message: BrowserCapabilityMessage) {
        requireWire(message.apiLevel in 1..0xffff, "Browser API level is out of range")
        requireWire(message.webViewVersion.isNotBlank(), "Browser WebView version is blank")
        requireTextLength(message.webViewVersion, BrowserWireLimits.MAX_TITLE_BYTES, "Browser WebView version")
        requireTextLength(message.detail, BrowserWireLimits.MAX_DETAIL_BYTES, "Browser capability detail")
        requireWire(message.secureEndpointPort in 0..0xffff, "Browser endpoint port is out of range")
        if (message.status == BrowserCapabilityStatus.AVAILABLE) {
            requireWire(message.secureEndpointPort > 0, "Available browser capability requires a secure endpoint")
        }

        if (!message.previewSupported) {
            requireWire(
                message.previewMaxWidth == 0 && message.previewMaxHeight == 0 &&
                    message.interactivePreviewFramesPerSecond == 0 &&
                    message.idlePreviewFramesPerSecond == 0 && message.previewMaxBytes == 0,
                "Unavailable preview must advertise zero limits",
            )
            return
        }

        requireWire(
            message.previewMaxWidth in 1..BrowserWireLimits.MAX_PREVIEW_WIDTH,
            "Browser preview width is out of range",
        )
        requireWire(
            message.previewMaxHeight in 1..BrowserWireLimits.MAX_PREVIEW_HEIGHT,
            "Browser preview height is out of range",
        )
        requireWire(
            message.interactivePreviewFramesPerSecond in 1..BrowserWireLimits.MAX_INTERACTIVE_PREVIEW_FPS,
            "Browser interactive preview rate is out of range",
        )
        requireWire(
            message.idlePreviewFramesPerSecond in 1..BrowserWireLimits.MAX_IDLE_PREVIEW_FPS,
            "Browser idle preview rate is out of range",
        )
        requireWire(
            message.previewMaxBytes in 1..BrowserWireLimits.MAX_PREVIEW_BYTES,
            "Browser preview byte limit is out of range",
        )
    }

    private fun validate(message: BrowserCommandMessage) {
        requirePositive(message.epoch, "browser command epoch")
        requirePositive(message.commandId, "browser command ID")
        val requiresUrl = message.action == BrowserCommandAction.OPEN || message.action == BrowserCommandAction.NAVIGATE
        val requiresPreview = message.action == BrowserCommandAction.SET_PREVIEW_ENABLED
        requireWire(requiresUrl == (message.url != null), "Browser command URL has an invalid action combination")
        requireWire(
            requiresPreview == (message.previewEnabled != null),
            "Browser preview setting has an invalid action combination",
        )
        if (requiresUrl) {
            requireWire(message.url!!.isNotBlank(), "Browser command URL is blank")
            requireTextLength(message.url, BrowserWireLimits.MAX_URL_BYTES, "Browser command URL")
        }
    }

    private fun validate(message: BrowserInputMessage) {
        requirePositive(message.epoch, "browser input epoch")
        requirePositive(message.sequence, "browser input sequence")
        when (val event = message.event) {
            is BrowserPointerInput -> {
                validatePreviewReference(event.navigationId, event.frameId, event.x, event.y)
                requireWire(
                    event.buttons == 0 || event.buttons == 1,
                    "Browser pointer buttons must be the primary bit or zero",
                )
                requireWire(
                    when (event.action) {
                        BrowserPointerAction.DOWN -> event.buttons == 1
                        BrowserPointerAction.UP, BrowserPointerAction.CANCEL -> event.buttons == 0
                        BrowserPointerAction.MOVE -> true
                    },
                    "Browser pointer action has an invalid button transition",
                )
            }

            is BrowserScrollInput -> {
                validatePreviewReference(event.navigationId, event.frameId, event.x, event.y)
                requireWire(event.deltaX != 0 || event.deltaY != 0, "Browser scroll must have a delta")
            }

            is BrowserSemanticKeyInput -> Unit
            is BrowserTextInput -> requireTextLength(event.text, BrowserWireLimits.MAX_TEXT_BYTES, "Browser text input")
        }
    }

    private fun validate(message: BrowserStateMessage) {
        requirePositive(message.epoch, "browser state epoch")
        requirePositive(message.revision, "browser state revision")
        requireNonNegative(message.navigationId, "browser state navigation ID")
        requireNonNegative(message.lastAcceptedCommandId, "browser state command acknowledgement")
        requireNonNegative(message.lastAcceptedInputSequence, "browser state input acknowledgement")
        requireTextLength(message.url, BrowserWireLimits.MAX_URL_BYTES, "Browser state URL")
        requireTextLength(message.title, BrowserWireLimits.MAX_TITLE_BYTES, "Browser state title")
        requireTextLength(message.errorDetail, BrowserWireLimits.MAX_DETAIL_BYTES, "Browser state error detail")
        requireWire(message.progress in 0..100, "Browser state progress is out of range")
        requireWire(message.viewportWidth in 0..MAX_VIEWPORT_DIMENSION, "Browser viewport width is out of range")
        requireWire(message.viewportHeight in 0..MAX_VIEWPORT_DIMENSION, "Browser viewport height is out of range")
        if (message.loadState == BrowserLoadState.FAILED) {
            requireWire(message.errorDetail.isNotBlank(), "Failed browser state requires an error detail")
        } else {
            requireWire(message.errorDetail.isEmpty(), "Only failed browser state carries an error detail")
        }
        if (message.loadState in setOf(BrowserLoadState.LOADING, BrowserLoadState.LOADED, BrowserLoadState.FAILED)) {
            requirePositive(message.navigationId, "active browser state navigation ID")
            requireWire(message.url.isNotBlank(), "Active browser state URL is blank")
        }
    }

    private fun validate(message: BrowserPreviewMessage) {
        requirePositive(message.epoch, "browser preview epoch")
        requirePositive(message.navigationId, "browser preview navigation ID")
        requirePositive(message.frameId, "browser preview frame ID")
        requireWire(
            message.width in 1..BrowserWireLimits.MAX_PREVIEW_WIDTH,
            "Browser preview width is out of range",
        )
        requireWire(
            message.height in 1..BrowserWireLimits.MAX_PREVIEW_HEIGHT,
            "Browser preview height is out of range",
        )
        requireWire(
            message.jpeg.size in 1..BrowserWireLimits.MAX_PREVIEW_BYTES,
            "Browser preview JPEG length is out of range",
        )
    }

    private fun validate(message: BrowserDialogMessage) {
        requirePositive(message.epoch, "browser dialog epoch")
        requirePositive(message.dialogId, "browser dialog ID")
        requireWire(message.origin.isNotBlank(), "Browser dialog origin is blank")
        requireTextLength(message.origin, BrowserWireLimits.MAX_ORIGIN_BYTES, "Browser dialog origin")
        requireTextLength(message.message, BrowserWireLimits.MAX_DIALOG_BYTES, "Browser dialog message")
        requireTextLength(message.defaultValue, BrowserWireLimits.MAX_DIALOG_BYTES, "Browser dialog default value")
        requireWire(
            message.timeoutMilliseconds in 1..BrowserWireLimits.MAX_DIALOG_TIMEOUT_MILLISECONDS,
            "Browser dialog timeout is out of range",
        )
        if (message.type != BrowserDialogType.PROMPT) {
            requireWire(message.defaultValue.isEmpty(), "Only browser prompts carry a default value")
        }
    }

    private fun validate(message: BrowserDialogReplyMessage) {
        requirePositive(message.epoch, "browser dialog reply epoch")
        requirePositive(message.dialogId, "browser dialog reply ID")
        if (!message.accepted) {
            requireWire(message.promptText == null, "Cancelled browser dialog reply carries prompt text")
        } else if (message.promptText != null) {
            requireTextLength(message.promptText, BrowserWireLimits.MAX_TEXT_BYTES, "Browser dialog prompt text")
        }
    }

    private fun validatePreviewReference(navigationId: Long, frameId: Long, x: Int, y: Int) {
        requirePositive(navigationId, "browser input navigation ID")
        requirePositive(frameId, "browser input frame ID")
        requireWire(x in 0..0xffff, "Browser input X is out of range")
        requireWire(y in 0..0xffff, "Browser input Y is out of range")
    }

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

    private data class BrowserPreviewReference(
        val navigationId: Long,
        val frameId: Long,
        val x: Int,
        val y: Int,
    )
}
