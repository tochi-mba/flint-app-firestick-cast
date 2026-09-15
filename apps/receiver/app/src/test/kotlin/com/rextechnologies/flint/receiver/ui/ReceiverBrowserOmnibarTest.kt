package com.rextechnologies.flint.receiver.ui

import androidx.activity.ComponentActivity
import androidx.compose.runtime.remember
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.BrowserSurfaceUi
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import kotlin.test.assertEquals

/**
 * The chrome has to be operable the moment it appears.
 *
 * Found on a Fire TV Stick 4K: pressing Menu on a freshly opened page showed the omnibar with
 * nothing focused, and the D-pad then did nothing at all — the surface had handed focus to the
 * chrome, and the chrome had handed it to a control that could not accept it.
 *
 * A later Fire TV defect: Select showed a press animation and dropped the halo without firing
 * [onAction], because [tvFocus] and [clickable] both owned a focus target. Covered here by asserting
 * a normal click reaches the action callback.
 */
@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverBrowserOmnibarTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Test
    fun `the control focus lands on is reachable with no history to go back to`() {
        // The state every newly opened page is in: Back and Forward are both disabled. Whatever the
        // surface focuses first has to be something that can actually take focus and fire on a
        // normal Select — not a long-press.
        val first = FocusRequester()
        var action: OmnibarAction? = null
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserOmnibar(
                    page = BrowserSurfaceUi(
                        url = "https://example.test/page",
                        title = "Example",
                        canGoBack = false,
                        canGoForward = false,
                    ),
                    tabCount = 1,
                    activeTab = 1,
                    firstControl = first,
                    onAction = { action = it },
                )
            }
        }

        compose.waitForIdle()
        compose.runOnUiThread { first.requestFocus() }
        compose.waitForIdle()
        compose.onNodeWithTag(ReceiverTags.BROWSER_SECURITY_CHIP)
            .assertIsDisplayed()
            .performClick()
        compose.waitForIdle()

        assertEquals(OmnibarAction.ADDRESS, action)
    }

    @Test
    fun `a normal click on the menu control fires the menu action`() {
        var action: OmnibarAction? = null
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserOmnibar(
                    page = BrowserSurfaceUi(url = "https://example.test/"),
                    tabCount = 1,
                    activeTab = 1,
                    firstControl = androidx.compose.runtime.remember { FocusRequester() },
                    onAction = { action = it },
                )
            }
        }

        compose.waitForIdle()
        compose.onNodeWithContentDescription("Browser menu").performClick()
        compose.waitForIdle()

        assertEquals(OmnibarAction.MENU, action)
    }

    @Test
    fun `the omnibar still renders when there is no address yet`() {
        compose.setContent {
            val first = remember { FocusRequester() }
            ReceiverTheme {
                ReceiverBrowserOmnibar(
                    page = BrowserSurfaceUi(),
                    tabCount = 1,
                    activeTab = 1,
                    firstControl = first,
                    onAction = {},
                )
            }
        }

        compose.waitForIdle()

        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.BROWSER_SECURITY_CHIP).assertIsDisplayed()
    }
}
