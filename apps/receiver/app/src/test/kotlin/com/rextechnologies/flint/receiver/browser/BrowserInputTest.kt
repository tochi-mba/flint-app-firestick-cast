package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs
import kotlin.test.assertTrue

class BrowserInputTest {
    private val mapper = BrowserInputMapper()
    private val reducer = BrowserInputReducer(mapper)

    @Test
    fun `maps every portable semantic key to a symbolic native key without platform codes`() {
        BrowserSemanticKey.entries.forEach { key ->
            val mapped = assertIs<BrowserInputMapping.Accepted>(mapper.map(BrowserInput.Semantic(7, 1, key)))
            assertIs<BrowserNativeInput.KeyStroke>(mapped.input)
        }

        val shiftTab = assertIs<BrowserInputMapping.Accepted>(mapper.map(BrowserInput.Semantic(7, 1, BrowserSemanticKey.SHIFT_TAB)))
        assertTrue(assertIs<BrowserNativeInput.KeyStroke>(shiftTab.input).shift)
    }

    @Test
    fun `accepts composed unicode text and redacts it from string representation`() {
        val mapping = assertIs<BrowserInputMapping.Accepted>(mapper.map(BrowserInput.Text(7, 1, "Á 你好 🚀")))
        val native = assertIs<BrowserNativeInput.ComposedText>(mapping.input)

        assertTrue(native.text.utf8Bytes > 0)
        assertTrue(!native.toString().contains("你好"))
        assertTrue(!native.text.toString().contains("你好"))
    }

    @Test
    fun `rejects empty controls broken unicode and one byte over text without echoing it`() {
        val values = listOf("", "abc\u0000def", "abc\n", "\ud800", "x".repeat(BrowserTextPolicy.MAX_UTF8_BYTES + 1))
        values.forEach { value ->
            val rejected = assertIs<BrowserInputMapping.Rejected>(mapper.map(BrowserInput.Text(7, 1, value)))
            assertTrue(rejected.toString().contains("redacted"))
            // Every String contains the empty substring, so only non-empty hostile input can prove
            // that the representation did not echo a user-controlled prefix.
            if (value.isNotEmpty()) {
                assertTrue(!rejected.toString().contains(value.take(3)))
            }
        }
    }

    @Test
    fun `input reducer rejects wrong epoch duplicate sequence disabled remote and non browser surface`() {
        val state = BrowserInputState(epoch = 4, lastSequence = 2, remoteInputEnabled = true, surface = BrowserSurfaceOwner.BROWSER)
        val accepted = reducer.reduce(state, BrowserInput.Semantic(4, 3, BrowserSemanticKey.SELECT))
        assertIs<BrowserInputMapping.Accepted>(accepted.mapping)
        assertEquals(3, accepted.state.lastSequence)

        val invalids = listOf(
            BrowserInput.Semantic(4, 3, BrowserSemanticKey.SELECT),
            BrowserInput.Semantic(3, 4, BrowserSemanticKey.SELECT),
        )
        invalids.forEach { input ->
            assertIs<BrowserInputMapping.Rejected>(reducer.reduce(accepted.state, input).mapping)
        }
        assertIs<BrowserInputMapping.Rejected>(
            reducer.reduce(state.copy(remoteInputEnabled = false), BrowserInput.Semantic(4, 3, BrowserSemanticKey.SELECT)).mapping,
        )
        assertIs<BrowserInputMapping.Rejected>(
            reducer.reduce(state.copy(surface = BrowserSurfaceOwner.MIRROR), BrowserInput.Semantic(4, 3, BrowserSemanticKey.SELECT)).mapping,
        )
    }
}
