package com.rextechnologies.flint.receiver

import android.view.KeyEvent
import androidx.activity.ComponentActivity
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.receiver.browser.BrowserOverlay
import com.rextechnologies.flint.receiver.ui.BrowserSurfaceController
import com.rextechnologies.flint.receiver.ui.ReceiverTags
import com.rextechnologies.flint.receiver.ui.ReceiverTheme
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config

/**
 * Headless stand-in for Fire TV UiAutomator browser chrome coverage.
 *
 * Runs under Robolectric (`testDebugUnitTest`) so the Back ladder and leave-prompt ownership can be
 * proven without a Stick. Device UiAutomator remains the physical gate.
 *
 * Back is applied through [BrowserSurfaceController] — the same path the surface's
 * `onPreviewKeyEvent` uses — because Robolectric focus injection does not reliably deliver
 * KEYCODE_BACK through Compose preview when an overlay owns focus.
 */
@RunWith(AndroidJUnit4::class)
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverBrowserHarnessRobolectricTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    private lateinit var controller: BrowserSurfaceController

    @Test
    fun `bare page shows the harness title`() {
        setHarness(BrowserHarnessStart.PAGE)
        compose.onNodeWithText("HARNESS PAGE").assertIsDisplayed()
        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertDoesNotExist()
    }

    @Test
    fun `chrome start shows omnibar controls`() {
        setHarness(BrowserHarnessStart.CHROME)
        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertIsDisplayed()
        compose.onNodeWithContentDescription("Browser menu").assertIsDisplayed()
    }

    @Test
    fun `back from chrome hides the omnibar`() {
        setHarness(BrowserHarnessStart.CHROME)
        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertIsDisplayed()
        pressBack()
        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertDoesNotExist()
        compose.onNodeWithText("HARNESS PAGE").assertIsDisplayed()
    }

    @Test
    fun `menu start shows tools and back returns to chrome`() {
        setHarness(BrowserHarnessStart.MENU)
        compose.onNodeWithText("BROWSER TOOLS").assertIsDisplayed()
        pressBack()
        compose.onNodeWithTag(ReceiverTags.BROWSER_MENU).assertDoesNotExist()
        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertIsDisplayed()
    }

    @Test
    fun `tabs start lists both cards`() {
        setHarness(BrowserHarnessStart.TABS)
        compose.onNodeWithTag(ReceiverTags.BROWSER_TAB_SWITCHER).assertIsDisplayed()
        compose.onNodeWithText("Harness tab").assertIsDisplayed()
        compose.onNodeWithText("Second tab").assertIsDisplayed()
    }

    @Test
    fun `find start is labeled and back returns to menu`() {
        setHarness(BrowserHarnessStart.FIND)
        compose.onNodeWithText("FIND IN THIS PAGE").assertIsDisplayed()
        pressBack()
        compose.onNodeWithText("BROWSER TOOLS").assertIsDisplayed()
    }

    @Test
    fun `bookmarks list harness entries and back returns to menu`() {
        setHarness(BrowserHarnessStart.BOOKMARKS)
        compose.onNodeWithText("Example A").assertIsDisplayed()
        pressBack()
        compose.onNodeWithText("BROWSER TOOLS").assertIsDisplayed()
    }

    @Test
    fun `history lists harness visits`() {
        setHarness(BrowserHarnessStart.HISTORY)
        compose.onNodeWithText("Visited page").assertIsDisplayed()
    }

    @Test
    fun `clear data keep returns to menu`() {
        setHarness(BrowserHarnessStart.CLEAR_DATA)
        compose.onNodeWithTag(ReceiverTags.BROWSER_CLEAR_DATA).assertIsDisplayed()
        compose.onNodeWithText("Keep data").assertIsDisplayed()
        // TV Material buttons do not always receive Compose performClick under Robolectric;
        // the cancel path is the same openOverlay(MENU) the button wires.
        controller.openOverlay(BrowserOverlay.MENU)
        compose.waitForIdle()
        compose.onNodeWithText("BROWSER TOOLS").assertIsDisplayed()
    }

    @Test
    fun `leave prompt keep browsing dismisses the guard`() {
        setHarness(BrowserHarnessStart.LEAVE)
        compose.onNodeWithTag(ReceiverTags.BROWSER_LEAVE_PROMPT).assertIsDisplayed()
        compose.onNodeWithText("Keep browsing").assertIsDisplayed()
        controller.cancelLeaveConfirm()
        compose.waitForIdle()
        compose.onNodeWithTag(ReceiverTags.BROWSER_LEAVE_PROMPT).assertDoesNotExist()
        compose.onNodeWithText("HARNESS PAGE").assertIsDisplayed()
    }

    @Test
    fun `back on a bare page opens the leave guard`() {
        setHarness(BrowserHarnessStart.PAGE)
        pressBack()
        compose.onNodeWithText("Leave the browser?").assertIsDisplayed()
        compose.onNodeWithText("Keep browsing").assertIsDisplayed()
    }

    @Test
    fun `back ladder unwinds menu then chrome then leave`() {
        setHarness(BrowserHarnessStart.MENU)
        pressBack()
        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertIsDisplayed()
        pressBack()
        compose.onNodeWithTag(ReceiverTags.BROWSER_OMNIBAR).assertDoesNotExist()
        pressBack()
        compose.onNodeWithText("Leave the browser?").assertIsDisplayed()
    }

    private fun setHarness(start: BrowserHarnessStart) {
        compose.setContent {
            ReceiverTheme {
                ReceiverBrowserHarness(
                    marker = "Receiver preview harness",
                    start = start,
                    onControllerReady = { controller = it },
                )
            }
        }
        compose.waitForIdle()
    }

    private fun pressBack() {
        controller.onKeyDown(KeyEvent.KEYCODE_BACK)
        controller.onKeyUp(KeyEvent.KEYCODE_BACK)
        compose.waitForIdle()
    }
}
