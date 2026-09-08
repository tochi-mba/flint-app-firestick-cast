package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.BrowserSurfaceUi
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config

/**
 * The behaviour of the chrome drawn over the television's browsing page.
 *
 * The chrome exists to answer three questions — what is this page, is anything happening, and what
 * went wrong — and then to get out of the way. These tests hold it to both halves of that: that it
 * says the right thing, and that it actually leaves.
 */
@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverBrowserChromeTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Before
    fun takeControlOfTheClock() {
        // The chrome hides itself on a timer and fades while it does. An auto-advancing clock means
        // every assertion races that fade; driving it by hand makes the timings the thing under
        // test rather than a source of flakes.
        compose.mainClock.autoAdvance = false
    }

    @Test
    fun `a loading page shows its address, its progress and that it is loading`() {
        compose.setContent {
            ReceiverBrowserChrome(
                page = BrowserSurfaceUi(
                    url = "example.com",
                    title = "Example Domain",
                    progressPercent = 40,
                    isLoading = true,
                ),
                autoHideMillis = null,
            )
        }
        compose.mainClock.advanceTimeBy(ANIMATION_SETTLE_MILLIS)

        compose.onNodeWithTag(ReceiverTags.BROWSER_TITLE).assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.BROWSER_URL).assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.BROWSER_PROGRESS).assertIsDisplayed()
        compose.onNodeWithText("LOADING").assertIsDisplayed()
    }

    @Test
    fun `a page with no title yet says so rather than showing an empty line`() {
        // The title arrives after the navigation does, so this is the ordinary first state of every
        // page rather than an edge case. A blank line reads as a broken layout.
        compose.setContent {
            ReceiverBrowserChrome(
                page = BrowserSurfaceUi(url = "example.com", isLoading = true),
                autoHideMillis = null,
            )
        }
        compose.mainClock.advanceTimeBy(ANIMATION_SETTLE_MILLIS)

        compose.onNodeWithText("Loading").assertIsDisplayed()
    }

    @Test
    fun `a settled page loses its progress bar`() {
        // A finished page with a progress bar still on it says the opposite of the truth.
        compose.setContent {
            ReceiverBrowserChrome(
                page = BrowserSurfaceUi(url = "example.com", title = "Example Domain", progressPercent = 100),
                autoHideMillis = null,
            )
        }
        compose.mainClock.advanceTimeBy(ANIMATION_SETTLE_MILLIS)

        compose.onNodeWithTag(ReceiverTags.BROWSER_PROGRESS).assertDoesNotExist()
        compose.onNodeWithText("WEB").assertIsDisplayed()
    }

    @Test
    fun `the chrome withdraws once the page is readable`() {
        // The whole reason it is allowed on screen at all: it goes away. Chrome that never leaves
        // is a permanent bite out of somebody else's page.
        compose.setContent {
            ReceiverBrowserChrome(
                page = BrowserSurfaceUi(url = "example.com", title = "Example Domain"),
                autoHideMillis = CHROME_AUTO_HIDE_MILLIS,
            )
        }

        compose.mainClock.advanceTimeBy(ANIMATION_SETTLE_MILLIS)
        compose.onNodeWithTag(ReceiverTags.BROWSER_CHROME).assertIsDisplayed()

        compose.mainClock.advanceTimeBy(CHROME_AUTO_HIDE_MILLIS + ANIMATION_SETTLE_MILLIS)
        compose.onNodeWithTag(ReceiverTags.BROWSER_CHROME).assertDoesNotExist()
    }

    @Test
    fun `a loading page keeps its chrome past the timeout`() {
        // A page that takes longer than the hide delay is exactly the page whose viewer most needs
        // to be told something is still happening.
        compose.setContent {
            ReceiverBrowserChrome(
                page = BrowserSurfaceUi(url = "slow.example", title = "Slow", isLoading = true),
                autoHideMillis = CHROME_AUTO_HIDE_MILLIS,
            )
        }

        compose.mainClock.advanceTimeBy(CHROME_AUTO_HIDE_MILLIS * 3)

        compose.onNodeWithTag(ReceiverTags.BROWSER_CHROME).assertIsDisplayed()
    }

    @Test
    fun `a failure keeps its chrome, because it is the only explanation on screen`() {
        compose.setContent {
            ReceiverBrowserChrome(
                page = BrowserSurfaceUi(
                    url = "blocked.example",
                    title = "Blocked",
                    failure = "That address was blocked. Try a different one from the desktop.",
                ),
                autoHideMillis = CHROME_AUTO_HIDE_MILLIS,
            )
        }

        compose.mainClock.advanceTimeBy(CHROME_AUTO_HIDE_MILLIS * 3)

        compose.onNodeWithTag(ReceiverTags.BROWSER_ERROR).assertIsDisplayed()
        compose.onNodeWithText("BLOCKED").assertIsDisplayed()
    }

    @Test
    fun `navigating to another page brings the chrome back`() {
        // Without this the chrome hides once and never returns, so every page after the first one
        // is anonymous — which is worse than having no chrome at all, because it is inconsistent.
        var page by mutableStateOf(BrowserSurfaceUi(url = "example.com", title = "Example Domain"))
        compose.setContent {
            ReceiverBrowserChrome(page = page, autoHideMillis = CHROME_AUTO_HIDE_MILLIS)
        }

        compose.mainClock.advanceTimeBy(CHROME_AUTO_HIDE_MILLIS + ANIMATION_SETTLE_MILLIS)
        compose.onNodeWithTag(ReceiverTags.BROWSER_CHROME).assertDoesNotExist()

        // `runOnIdle` rather than a bare assignment: a state write from the test thread is not
        // published to the composition until the snapshot is applied, and with the clock held still
        // nothing else does that — the chrome would look permanently gone and the test would be
        // reporting the harness rather than the code.
        compose.runOnIdle { page = BrowserSurfaceUi(url = "another.example", title = "Another", isLoading = true) }
        // Then a frame, by hand. With the clock held still nothing else produces one, and the
        // recomposition that brings the chrome back only happens on a frame — without this the
        // chrome looks permanently gone and the test reports the harness rather than the code.
        compose.mainClock.advanceTimeByFrame()
        compose.waitForIdle()
        compose.mainClock.advanceTimeBy(ANIMATION_SETTLE_MILLIS)

        compose.onNodeWithTag(ReceiverTags.BROWSER_CHROME).assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.BROWSER_URL).assertIsDisplayed()
    }

    private companion object {
        /** Long enough for a fade to finish, so an assertion never lands mid-animation. */
        const val ANIMATION_SETTLE_MILLIS = 1_000L
    }
}
