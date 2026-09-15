package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

/**
 * The seven original browser message types, which had no test of their own.
 *
 * Every one of them is decoded from bytes a remote sent, so each guard in the codec is a claim
 * about what the receiver refuses. A guard nothing exercises is a guard nobody has checked, and
 * these are the guards standing between a television and a malformed frame.
 */
class BrowserWireCodecTest {
    @Test
    fun `IDs 14 through 20 and every phase-one enum value round trip`() {
        val messages = buildList<WireMessage> {
            BrowserCapabilityStatus.entries.forEach { status ->
                add(capability(status = status))
            }
            add(capability(previewSupported = false))
            BrowserCommandAction.entries.forEach { action -> add(command(action)) }
            BrowserPointerAction.entries.forEach { action ->
                add(BrowserInputMessage(2, 1, pointer(action)))
            }
            BrowserSemanticKey.entries.forEach { key ->
                add(BrowserInputMessage(2, 1, BrowserSemanticKeyInput(key)))
            }
            add(BrowserInputMessage(2, 1, BrowserScrollInput(1, 1, 10, 20, 0, -3)))
            add(BrowserInputMessage(2, 1, BrowserTextInput("hello")))
            BrowserLoadState.entries.forEach { state -> add(state(state)) }
            BrowserPreviewState.entries.forEach { preview ->
                add(state(BrowserLoadState.LOADED, previewState = preview))
            }
            add(BrowserPreviewMessage(2, 1, 1, 320, 180, BinaryData.of(byteArrayOf(1, 2, 3))))
            BrowserDialogType.entries.forEach { type -> add(dialog(type)) }
            add(BrowserDialogReplyMessage(2, 1, accepted = true, promptText = "typed"))
            add(BrowserDialogReplyMessage(2, 1, accepted = true, promptText = null))
            add(BrowserDialogReplyMessage(2, 1, accepted = false))
        }

        assertEquals((14..20).toSet(), messages.map { it.typeId }.toSet())
        messages.forEach { message ->
            val frame = WireFrame(2, message)
            assertEquals(frame, WireCodec.decode(WireCodec.encode(frame)), message.toString())
            assertTrue(BrowserWireRules.isForbiddenOnOrdinaryChannel(message), message.toString())
        }
    }

    @Test
    fun `every phase-one guard rejects the value it names`() {
        val tooLong = "a".repeat(BrowserWireLimits.MAX_URL_BYTES + 1)
        val invalid = listOf<Pair<String, WireMessage>>(
            "api level zero" to capability(apiLevel = 0),
            "api level above u16" to capability(apiLevel = 0x1_0000),
            "blank WebView version" to capability(webViewVersion = "  "),
            "WebView version too long" to capability(webViewVersion = "v".repeat(513)),
            "detail too long" to capability(detail = "d".repeat(1025)),
            "port above u16" to capability(secureEndpointPort = 0x1_0000),
            "available without a port" to capability(
                status = BrowserCapabilityStatus.AVAILABLE,
                secureEndpointPort = 0,
            ),
            "unsupported preview with limits" to capability().copy(previewSupported = false),
            "preview width zero" to capability(previewMaxWidth = 0),
            "preview width too wide" to capability(previewMaxWidth = BrowserWireLimits.MAX_PREVIEW_WIDTH + 1),
            "preview height zero" to capability(previewMaxHeight = 0),
            "preview height too tall" to capability(previewMaxHeight = BrowserWireLimits.MAX_PREVIEW_HEIGHT + 1),
            "interactive rate zero" to capability(interactivePreviewFramesPerSecond = 0),
            "interactive rate too fast" to capability(
                interactivePreviewFramesPerSecond = BrowserWireLimits.MAX_INTERACTIVE_PREVIEW_FPS + 1,
            ),
            "idle rate zero" to capability(idlePreviewFramesPerSecond = 0),
            "idle rate too fast" to capability(
                idlePreviewFramesPerSecond = BrowserWireLimits.MAX_IDLE_PREVIEW_FPS + 1,
            ),
            "preview bytes zero" to capability(previewMaxBytes = 0),
            "preview bytes too large" to capability(previewMaxBytes = BrowserWireLimits.MAX_PREVIEW_BYTES + 1),

            "command epoch zero" to command(BrowserCommandAction.BACK).copy(epoch = 0),
            "command id zero" to command(BrowserCommandAction.BACK).copy(commandId = 0),
            "navigate without a URL" to BrowserCommandMessage(2, 1, BrowserCommandAction.NAVIGATE),
            "back carrying a URL" to BrowserCommandMessage(2, 1, BrowserCommandAction.BACK, "https://a.test"),
            "blank command URL" to BrowserCommandMessage(2, 1, BrowserCommandAction.OPEN, "   "),
            "command URL too long" to BrowserCommandMessage(2, 1, BrowserCommandAction.OPEN, tooLong),
            "preview toggle without a value" to BrowserCommandMessage(
                2,
                1,
                BrowserCommandAction.SET_PREVIEW_ENABLED,
            ),
            "back carrying a preview value" to BrowserCommandMessage(
                2,
                1,
                BrowserCommandAction.BACK,
                previewEnabled = true,
            ),

            "input epoch zero" to BrowserInputMessage(0, 1, pointer(BrowserPointerAction.MOVE)),
            "input sequence zero" to BrowserInputMessage(2, 0, pointer(BrowserPointerAction.MOVE)),
            "pointer navigation zero" to BrowserInputMessage(
                2,
                1,
                pointer(BrowserPointerAction.MOVE).copy(navigationId = 0),
            ),
            "pointer frame zero" to BrowserInputMessage(2, 1, pointer(BrowserPointerAction.MOVE).copy(frameId = 0)),
            "pointer x negative" to BrowserInputMessage(2, 1, pointer(BrowserPointerAction.MOVE).copy(x = -1)),
            "pointer y above u16" to BrowserInputMessage(
                2,
                1,
                pointer(BrowserPointerAction.MOVE).copy(y = 0x1_0000),
            ),
            "pointer secondary button" to BrowserInputMessage(
                2,
                1,
                pointer(BrowserPointerAction.MOVE).copy(buttons = 2),
            ),
            "pointer down without the button" to BrowserInputMessage(
                2,
                1,
                pointer(BrowserPointerAction.DOWN).copy(buttons = 0),
            ),
            "pointer up holding the button" to BrowserInputMessage(
                2,
                1,
                pointer(BrowserPointerAction.UP).copy(buttons = 1),
            ),
            "scroll without a delta" to BrowserInputMessage(2, 1, BrowserScrollInput(1, 1, 0, 0, 0, 0)),
            "scroll navigation zero" to BrowserInputMessage(2, 1, BrowserScrollInput(0, 1, 0, 0, 1, 0)),
            "text input too long" to BrowserInputMessage(
                2,
                1,
                BrowserTextInput("t".repeat(BrowserWireLimits.MAX_TEXT_BYTES + 1)),
            ),

            "state epoch zero" to state().copy(epoch = 0),
            "state revision zero" to state().copy(revision = 0),
            "state navigation negative" to state(BrowserLoadState.IDLE).copy(navigationId = -1),
            "command acknowledgement negative" to state().copy(lastAcceptedCommandId = -1),
            "input acknowledgement negative" to state().copy(lastAcceptedInputSequence = -1),
            "state URL too long" to state().copy(url = tooLong),
            "state title too long" to state().copy(title = "t".repeat(513)),
            "progress above a hundred" to state().copy(progress = 101),
            "progress negative" to state().copy(progress = -1),
            "viewport too wide" to state().copy(viewportWidth = 16_385),
            "viewport too tall" to state().copy(viewportHeight = 16_385),
            "failed without a detail" to state(BrowserLoadState.FAILED).copy(errorDetail = ""),
            "loaded carrying a detail" to state(BrowserLoadState.LOADED).copy(errorDetail = "boom"),
            "error detail too long" to state(BrowserLoadState.FAILED).copy(errorDetail = "e".repeat(1025)),
            "active state without a navigation" to state(BrowserLoadState.LOADED).copy(navigationId = 0),
            "active state with a blank URL" to state(BrowserLoadState.LOADED).copy(url = " "),

            "preview epoch zero" to preview().copy(epoch = 0),
            "preview navigation zero" to preview().copy(navigationId = 0),
            "preview frame zero" to preview().copy(frameId = 0),
            "preview width zero" to preview().copy(width = 0),
            "preview width too wide" to preview().copy(width = BrowserWireLimits.MAX_PREVIEW_WIDTH + 1),
            "preview height zero" to preview().copy(height = 0),
            "preview height too tall" to preview().copy(height = BrowserWireLimits.MAX_PREVIEW_HEIGHT + 1),
            "empty preview JPEG" to preview().copy(jpeg = BinaryData.EMPTY),

            "dialog epoch zero" to dialog().copy(epoch = 0),
            "dialog id zero" to dialog().copy(dialogId = 0),
            "blank dialog origin" to dialog().copy(origin = " "),
            "dialog origin too long" to dialog().copy(origin = "o".repeat(BrowserWireLimits.MAX_ORIGIN_BYTES + 1)),
            "dialog message too long" to dialog().copy(message = "m".repeat(BrowserWireLimits.MAX_DIALOG_BYTES + 1)),
            "dialog timeout zero" to dialog().copy(timeoutMilliseconds = 0),
            "dialog timeout too long" to dialog().copy(
                timeoutMilliseconds = BrowserWireLimits.MAX_DIALOG_TIMEOUT_MILLISECONDS + 1,
            ),
            "alert carrying a default value" to dialog(BrowserDialogType.ALERT).copy(defaultValue = "x"),
            "prompt default too long" to dialog(BrowserDialogType.PROMPT).copy(
                defaultValue = "d".repeat(BrowserWireLimits.MAX_DIALOG_BYTES + 1),
            ),

            "reply epoch zero" to BrowserDialogReplyMessage(0, 1, accepted = true),
            "reply id zero" to BrowserDialogReplyMessage(2, 0, accepted = true),
            "cancelled reply carrying text" to BrowserDialogReplyMessage(2, 1, accepted = false, promptText = "x"),
            "reply text too long" to BrowserDialogReplyMessage(
                2,
                1,
                accepted = true,
                promptText = "p".repeat(BrowserWireLimits.MAX_TEXT_BYTES + 1),
            ),
        )

        invalid.forEach { (name, message) ->
            assertFailsWith<WireFormatException>(name) { WireCodec.encode(WireFrame(2, message)) }
        }
    }

    @Test
    fun `phase-one messages reject v1 envelopes and trailing bytes`() {
        representatives().forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) { WireCodec.encode(WireFrame(1, message)) }

            val body = payload(message)
            assertFailsWith<WireFormatException>(message.toString()) {
                WireMessageCodec.decode(1, message.typeId, body)
            }
            assertFailsWith<WireFormatException>(message.toString()) {
                WireMessageCodec.decode(2, message.typeId, body + 0)
            }
            assertFailsWith<WireFormatException>(message.toString()) {
                WireMessageCodec.decode(2, message.typeId, body.copyOfRange(0, body.size - 1))
            }
        }
    }

    @Test
    fun `unknown phase-one enum tags are rejected on the way in`() {
        // Zero is a tag no entry claims. A decoder that trusted the byte would hand the
        // television a null where it expects a state. The offset is where that message's
        // encoder writes its enum: after two i64s for the ones that carry an epoch and an ID.
        val tagged = listOf(
            Triple("capability status", capability() as WireMessage, 0),
            Triple("command action", command(BrowserCommandAction.BACK), 16),
            Triple("input event kind", BrowserInputMessage(2, 1, pointer(BrowserPointerAction.MOVE)), 16),
            Triple("state load state", state(), 40),
            Triple("dialog type", dialog(), 16),
        )

        tagged.forEach { (name, message, offset) ->
            val bytes = payload(message).also { it[offset] = 0 }
            assertFailsWith<WireFormatException>(name) {
                WireMessageCodec.decode(2, message.typeId, bytes)
            }
        }
    }

    @Test
    fun `the codec refuses a type it does not own`() {
        assertFailsWith<WireFormatException> { BrowserWireCodec.encode(ByeMessage(ByeReason.NORMAL, "bye")) }
        assertFailsWith<WireFormatException> { BrowserWireCodec.decode(WireMessageType.BYE, ByteArray(0)) }
    }

    private fun representatives(): List<WireMessage> = listOf(
        capability(),
        command(BrowserCommandAction.BACK),
        BrowserInputMessage(2, 1, BrowserSemanticKeyInput(BrowserSemanticKey.SELECT)),
        state(),
        preview(),
        dialog(),
        BrowserDialogReplyMessage(2, 1, accepted = true, promptText = "typed"),
    )

    private fun payload(message: WireMessage): ByteArray {
        val encoded = WireCodec.encode(WireFrame(2, message))
        return encoded.copyOfRange(HEADER_BYTES, encoded.size)
    }

    private fun capability(
        status: BrowserCapabilityStatus = BrowserCapabilityStatus.AVAILABLE,
        secureEndpointPort: Int = 8443,
        apiLevel: Int = 34,
        webViewVersion: String = "129.0.0.0",
        previewSupported: Boolean = true,
        previewMaxWidth: Int = 960,
        previewMaxHeight: Int = 540,
        interactivePreviewFramesPerSecond: Int = 5,
        idlePreviewFramesPerSecond: Int = 1,
        previewMaxBytes: Int = 256 * 1024,
        detail: String = "",
    ) = BrowserCapabilityMessage(
        status = status,
        secureEndpointPort = secureEndpointPort,
        apiLevel = apiLevel,
        webViewVersion = webViewVersion,
        previewSupported = previewSupported,
        previewMaxWidth = if (previewSupported) previewMaxWidth else 0,
        previewMaxHeight = if (previewSupported) previewMaxHeight else 0,
        interactivePreviewFramesPerSecond = if (previewSupported) interactivePreviewFramesPerSecond else 0,
        idlePreviewFramesPerSecond = if (previewSupported) idlePreviewFramesPerSecond else 0,
        previewMaxBytes = if (previewSupported) previewMaxBytes else 0,
        detail = detail,
    )

    private fun command(action: BrowserCommandAction) = BrowserCommandMessage(
        epoch = 2,
        commandId = 1,
        action = action,
        url = if (action == BrowserCommandAction.OPEN || action == BrowserCommandAction.NAVIGATE) {
            "https://example.test/page"
        } else {
            null
        },
        previewEnabled = if (action == BrowserCommandAction.SET_PREVIEW_ENABLED) true else null,
    )

    private fun pointer(action: BrowserPointerAction) = BrowserPointerInput(
        action = action,
        navigationId = 1,
        frameId = 1,
        x = 100,
        y = 200,
        buttons = if (action == BrowserPointerAction.DOWN) 1 else 0,
    )

    private fun state(
        loadState: BrowserLoadState = BrowserLoadState.IDLE,
        previewState: BrowserPreviewState = BrowserPreviewState.DISABLED,
    ): BrowserStateMessage {
        val active = loadState == BrowserLoadState.LOADING ||
            loadState == BrowserLoadState.LOADED ||
            loadState == BrowserLoadState.FAILED
        return BrowserStateMessage(
            epoch = 2,
            revision = 1,
            navigationId = if (active) 1 else 0,
            lastAcceptedCommandId = 0,
            lastAcceptedInputSequence = 0,
            loadState = loadState,
            url = if (active) "https://example.test/page" else "",
            title = "Example",
            progress = 100,
            canGoBack = false,
            canGoForward = false,
            viewportWidth = 1920,
            viewportHeight = 1080,
            previewState = previewState,
            errorDetail = if (loadState == BrowserLoadState.FAILED) "Could not reach the site" else "",
        )
    }

    private fun preview() = BrowserPreviewMessage(2, 1, 1, 320, 180, BinaryData.of(byteArrayOf(1, 2, 3)))

    private fun dialog(type: BrowserDialogType = BrowserDialogType.PROMPT) = BrowserDialogMessage(
        epoch = 2,
        dialogId = 1,
        type = type,
        origin = "https://example.test",
        message = "Are you sure?",
        defaultValue = if (type == BrowserDialogType.PROMPT) "yes" else "",
        timeoutMilliseconds = 30_000,
    )

    private companion object {
        const val HEADER_BYTES = 12
    }
}
