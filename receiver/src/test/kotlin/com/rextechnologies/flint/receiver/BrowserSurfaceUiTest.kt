package com.rextechnologies.flint.receiver

import com.rextechnologies.flint.receiver.browser.BrowserAddress
import com.rextechnologies.flint.receiver.browser.BrowserFailure
import com.rextechnologies.flint.receiver.browser.BrowserPageTitle
import com.rextechnologies.flint.receiver.browser.BrowserPhase
import com.rextechnologies.flint.receiver.browser.BrowserState
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** What the television is allowed to learn about the page it is showing. */
class BrowserSurfaceUiTest {
    @Test
    fun `history availability reaches the chrome so back and forward can show their real state`() {
        // Without this the television cannot render a disabled Back button, because the projection
        // dropped both flags and the chrome had no way to know a history entry existed.
        val state = BrowserState(
            phase = BrowserPhase.READY,
            revision = 4,
            epoch = 1,
            navigationId = 2,
            address = BrowserAddress("https://example.test/two", "https://example.test/two"),
            canGoBack = true,
            canGoForward = false,
        )

        val surface = state.toSurfaceUi()

        assertTrue(surface.canGoBack)
        assertFalse(surface.canGoForward)
    }

    @Test
    fun `an untouched state offers no history in either direction`() {
        val surface = BrowserState().toSurfaceUi()

        assertFalse(surface.canGoBack)
        assertFalse(surface.canGoForward)
    }

    @Test
    fun `an untouched state shows nothing at all`() {
        // The state before any command arrives. Every field has to be empty rather than a
        // placeholder, because the chrome renders whatever it is handed.
        val surface = BrowserState().toSurfaceUi()

        assertEquals("", surface.url)
        assertEquals("", surface.title)
        assertEquals(0, surface.progressPercent)
        assertFalse(surface.isLoading)
        assertNull(surface.failure)
    }

    @Test
    fun `the display url is carried across rather than the canonical one`() {
        // The canonical form is what the WebView is told to load; the display form is what a person
        // can read. Showing punycode or a normalised form on a television helps nobody.
        val surface = BrowserState(
            address = BrowserAddress(
                canonicalUrl = "https://xn--bcher-kva.example/",
                displayUrl = "bücher.example",
            ),
        ).toSurfaceUi()

        assertEquals("bücher.example", surface.url)
    }

    @Test
    fun `opening and loading both count as loading`() {
        // A viewer cannot tell the two apart and should not have to: both mean the page is not
        // ready yet, which is the only thing the indicator claims.
        for (phase in listOf(BrowserPhase.OPENING, BrowserPhase.LOADING)) {
            assertTrue(
                BrowserState(phase = phase).toSurfaceUi().isLoading,
                "$phase should read as loading",
            )
        }
    }

    @Test
    fun `a settled page is not loading`() {
        for (phase in listOf(BrowserPhase.IDLE, BrowserPhase.READY, BrowserPhase.CLOSING, BrowserPhase.ERROR)) {
            assertFalse(
                BrowserState(phase = phase).toSurfaceUi().isLoading,
                "$phase should not read as loading",
            )
        }
    }

    @Test
    fun `progress outside the sane range is clamped rather than passed on`() {
        // The value originates in a page's own progress reporting, which is not something this
        // side controls. A negative or over-large percentage laid out a bar wider than its parent.
        assertEquals(0, BrowserState(progressPercent = -40).toSurfaceUi().progressPercent)
        assertEquals(100, BrowserState(progressPercent = 4_000).toSurfaceUi().progressPercent)
        assertEquals(63, BrowserState(progressPercent = 63).toSurfaceUi().progressPercent)
    }

    @Test
    fun `the title comes through as the page's own bounded text`() {
        val surface = BrowserState(title = BrowserPageTitle("Example Domain")).toSurfaceUi()

        assertEquals("Example Domain", surface.title)
    }

    @Test
    fun `every failure becomes a sentence a viewer can act on`() {
        // The point of this mapping: nobody standing at a television can look up what
        // RENDERER_STOPPED means, and a code on screen is the same as no message at all.
        for (failure in BrowserFailure.entries) {
            val sentence = BrowserState(failure = failure).toSurfaceUi().failure

            assertNotNull(sentence, "$failure should map to a sentence")
            assertTrue(sentence.isNotBlank(), "$failure produced blank text")
            assertTrue(sentence.first().isUpperCase(), "$failure should start as a sentence: $sentence")
            assertTrue(sentence.endsWith("."), "$failure should end as a sentence: $sentence")
            assertFalse(
                sentence.contains(failure.name),
                "$failure should not leak its enum name to the screen: $sentence",
            )
        }
    }

    @Test
    fun `distinct failures read distinctly`() {
        // A shared fallback string would make two different problems look like one, and the whole
        // reason the sentence exists is to tell the viewer which problem they have.
        val sentences = BrowserFailure.entries.map { BrowserState(failure = it).toSurfaceUi().failure }

        assertEquals(BrowserFailure.entries.size, sentences.toSet().size)
    }

    @Test
    fun `a blocked address still shows which address was blocked`() {
        // The failure and the address travel together: "that was blocked" is not useful without
        // the "that".
        val surface = BrowserState(
            phase = BrowserPhase.ERROR,
            address = BrowserAddress("https://blocked.example/", "blocked.example"),
            failure = BrowserFailure.BLOCKED_URL,
        ).toSurfaceUi()

        assertEquals("blocked.example", surface.url)
        assertNotNull(surface.failure)
        assertFalse(surface.isLoading)
    }
}
