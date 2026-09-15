package com.rextechnologies.flint.protocol.wire

import com.rextechnologies.flint.protocol.BinaryData
import java.nio.ByteBuffer
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertTrue

class BrowserWirePhase2CodecTest {
    @Test
    fun `IDs 21 through 27 and every new enum value round trip`() {
        val messages = buildList<WireMessage> {
            BrowserTabAction.entries.forEachIndexed { index, action ->
                add(
                    BrowserTabCommandMessage(
                        epoch = 2,
                        commandId = 21L + index,
                        action = action,
                        tabId = if (action == BrowserTabAction.NEW) 0 else 7L + index,
                        url = if (action == BrowserTabAction.NEW) "https://example.test/new" else null,
                    ),
                )
            }
            add(tabState())
            add(BrowserTabStateMessage(2, 32, 0, emptyList()))

            add(viewCommand(40, BrowserViewAction.SET_ZOOM, 125))
            BrowserUserAgentMode.entries.forEachIndexed { index, mode ->
                add(viewCommand(41L + index, BrowserViewAction.SET_UA, mode.id))
            }
            BrowserDarkMode.entries.forEachIndexed { index, mode ->
                add(viewCommand(44L + index, BrowserViewAction.SET_DARK, mode.id))
            }
            BrowserInteractionMode.entries.forEachIndexed { index, mode ->
                add(viewCommand(47L + index, BrowserViewAction.SET_INPUT_MODE, mode.id))
            }
            add(viewCommand(49, BrowserViewAction.SET_FULLSCREEN, 1))
            add(viewCommand(50, BrowserViewAction.FIND_START, text = "needle"))
            add(viewCommand(51, BrowserViewAction.FIND_NEXT))
            add(viewCommand(52, BrowserViewAction.FIND_PREV))
            add(viewCommand(53, BrowserViewAction.FIND_CLEAR))
            BrowserSearchEngine.entries.forEachIndexed { index, engine ->
                add(
                    viewCommand(
                        54L + index,
                        BrowserViewAction.SET_SEARCH_ENGINE,
                        engine.id,
                        if (engine == BrowserSearchEngine.CUSTOM) "https://search.example/?q={q}" else "",
                    ),
                )
            }
            add(viewState())
            add(favicon())

            BrowserLibraryAction.entries.forEachIndexed { index, action ->
                val url = if (action == BrowserLibraryAction.ADD_BOOKMARK ||
                    action == BrowserLibraryAction.REMOVE_BOOKMARK
                ) {
                    "https://example.test/bookmark"
                } else {
                    ""
                }
                add(
                    BrowserLibraryCommandMessage(
                        epoch = 2,
                        commandId = 70L + index,
                        action = action,
                        url = url,
                        title = if (action == BrowserLibraryAction.ADD_BOOKMARK) "Example" else "",
                    ),
                )
            }
            add(libraryState())
            add(BrowserLibraryStateMessage(2, 81, emptyList(), emptyList()))
        }

        assertEquals((21..27).toSet(), messages.map { it.typeId }.toSet())
        messages.forEach { message ->
            val frame = WireFrame(2, message)
            assertEquals(frame, WireCodec.decode(WireCodec.encode(frame)), message.toString())
            assertTrue(BrowserWireRules.isBrowserMessage(message), message.toString())
            assertTrue(BrowserWireRules.isBrowserType(message.typeId), message.toString())
            assertTrue(BrowserWireRules.isForbiddenOnOrdinaryChannel(message), message.toString())
        }
    }

    @Test
    fun `each additive browser type rejects v1 and trailing payload bytes`() {
        representatives().forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) {
                WireCodec.encode(WireFrame(1, message))
            }

            val payload = WireCodec.encode(WireFrame(2, message)).copyOfRange(12, WireCodec.encode(WireFrame(2, message)).size)
            assertFailsWith<WireFormatException>(message.toString()) {
                WireMessageCodec.decode(1, message.typeId, payload)
            }
            assertFailsWith<WireFormatException>(message.toString()) {
                WireMessageCodec.decode(2, message.typeId, payload + 0)
            }
        }
    }

    @Test
    fun `invalid phase-two enum tags are rejected`() {
        val invalidPayloads = listOf(
            WireMessageType.BROWSER_TAB_COMMAND to payload(BrowserTabCommandMessage(2, 21, BrowserTabAction.CLOSE, 7)).also {
                it[16] = 0
            },
            WireMessageType.BROWSER_TAB_STATE to payload(tabState()).also { it[33] = 0 },
            WireMessageType.BROWSER_VIEW_COMMAND to payload(viewCommand(40, BrowserViewAction.SET_ZOOM, 125)).also {
                it[16] = 0
            },
            WireMessageType.BROWSER_VIEW_STATE to payload(viewState()).also { it[20] = 0 },
            WireMessageType.BROWSER_VIEW_STATE to payload(viewState()).also { it[21] = 0 },
            WireMessageType.BROWSER_VIEW_STATE to payload(viewState()).also { it[22] = 0 },
            WireMessageType.BROWSER_VIEW_STATE to payload(viewState()).also { it[35] = 0 },
            WireMessageType.BROWSER_LIBRARY_COMMAND to payload(
                BrowserLibraryCommandMessage(2, 70, BrowserLibraryAction.ADD_BOOKMARK, "https://example.test", "Example"),
            ).also { it[16] = 0 },
            WireMessageType.BROWSER_LIBRARY_STATE to payload(libraryState()).also { it[18] = 0 },
        )

        invalidPayloads.forEach { (type, bytes) ->
            assertFailsWith<WireFormatException>(type.name) { WireMessageCodec.decode(2, type.id, bytes) }
        }
    }

    @Test
    fun `phase-two bounds and action invariants reject before encoding`() {
        val tooManyTabs = List(BrowserWireLimits.MAX_TABS + 1) { index ->
            BrowserTabStateEntry(index + 1L, BrowserLoadState.IDLE, 0, false, false, false, 0, "", "")
        }
        val invalid = listOf<WireMessage>(
            BrowserTabCommandMessage(2, 1, BrowserTabAction.NEW, 1),
            BrowserTabCommandMessage(2, 1, BrowserTabAction.CLOSE, 1, "https://example.test"),
            BrowserTabStateMessage(2, 1, 1, tooManyTabs),
            BrowserTabStateMessage(2, 1, 2, listOf(
                BrowserTabStateEntry(1, BrowserLoadState.LOADED, 101, false, false, false, 0, "https://example.test", ""),
            )),
            viewCommand(1, BrowserViewAction.SET_ZOOM, BrowserWireLimits.MIN_ZOOM_PERCENT - 1),
            viewCommand(1, BrowserViewAction.SET_UA, 99),
            viewCommand(1, BrowserViewAction.FIND_START),
            viewCommand(1, BrowserViewAction.SET_SEARCH_ENGINE, BrowserSearchEngine.CUSTOM.id),
            BrowserViewStateMessage(
                2, 1, 125, BrowserUserAgentMode.TV, BrowserDarkMode.LIGHT, BrowserInteractionMode.CURSOR,
                false, false, false, false, 1, 1, BrowserSearchEngine.DUCKDUCKGO,
            ),
            BrowserFaviconMessage(2, 1, 65, 1, BinaryData.of(byteArrayOf(1))),
            BrowserFaviconMessage(2, 1, 1, 1, BinaryData.EMPTY),
            BrowserLibraryCommandMessage(2, 1, BrowserLibraryAction.REMOVE_BOOKMARK, "https://example.test", "title"),
            BrowserLibraryCommandMessage(2, 1, BrowserLibraryAction.CLEAR_HISTORY, "https://example.test"),
            BrowserLibraryStateMessage(
                2,
                1,
                listOf(BrowserLibraryEntry(BrowserLibraryEntryKind.HISTORY, 0, 0, "https://example.test", "")),
                emptyList(),
            ),
        )

        invalid.forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) { WireCodec.encode(WireFrame(2, message)) }
        }
    }

    private fun representatives(): List<WireMessage> = listOf(
        BrowserTabCommandMessage(2, 21, BrowserTabAction.NEW, 0, "https://example.test/new"),
        tabState(),
        viewCommand(40, BrowserViewAction.SET_ZOOM, 125),
        viewState(),
        favicon(),
        BrowserLibraryCommandMessage(2, 70, BrowserLibraryAction.ADD_BOOKMARK, "https://example.test", "Example"),
        libraryState(),
    )

    private fun tabState(): BrowserTabStateMessage = BrowserTabStateMessage(
        2,
        31,
        7,
        listOf(
            BrowserTabStateEntry(7, BrowserLoadState.LOADED, 100, true, false, false, 41, "https://example.test/a", "Alpha"),
            BrowserTabStateEntry(8, BrowserLoadState.LOADING, 37, false, true, true, 0, "https://example.test/b", "Beta"),
        ),
    )

    private fun viewCommand(
        commandId: Long,
        action: BrowserViewAction,
        value: Int = 0,
        text: String = "",
    ): BrowserViewCommandMessage = BrowserViewCommandMessage(2, commandId, action, value, text)

    private fun viewState(): BrowserViewStateMessage = BrowserViewStateMessage(
        epoch = 2,
        revision = 60,
        zoomPercent = 125,
        userAgentMode = BrowserUserAgentMode.DESKTOP,
        darkMode = BrowserDarkMode.DARK,
        inputMode = BrowserInteractionMode.FOCUS,
        fullscreen = true,
        mediaPlaying = true,
        editingFocused = true,
        findActive = true,
        findCurrent = 2,
        findTotal = 5,
        searchEngine = BrowserSearchEngine.BING,
    )

    private fun favicon(): BrowserFaviconMessage = BrowserFaviconMessage(
        2,
        61,
        2,
        2,
        BinaryData.of(byteArrayOf(0x89.toByte(), 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)),
    )

    private fun libraryState(): BrowserLibraryStateMessage = BrowserLibraryStateMessage(
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
    )

    private fun payload(message: WireMessage): ByteArray =
        WireCodec.encode(WireFrame(2, message)).let { it.copyOfRange(12, it.size) }
}
