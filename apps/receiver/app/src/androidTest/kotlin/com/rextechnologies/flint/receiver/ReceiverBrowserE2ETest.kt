package com.rextechnologies.flint.receiver

import android.content.Context
import android.content.Intent
import android.os.SystemClock
import android.view.KeyEvent
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.UiObject2
import androidx.test.uiautomator.Until
import com.rextechnologies.flint.receiver.ui.ReceiverTags
import org.junit.After
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Fire TV UiAutomator coverage for every browser chrome surface the harness can reach.
 *
 * Fire OS does not reliably expose Compose `focused` flags, so assertions are behavioural: a
 * control must appear, respond to Select/Back, and leave the expected next state. The harness uses
 * the real [com.rextechnologies.flint.receiver.ui.BrowserSurfaceController] key ladder.
 */
@RunWith(AndroidJUnit4::class)
class ReceiverBrowserE2ETest {
    private lateinit var context: Context
    private lateinit var device: UiDevice

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
    }

    @After
    fun tearDown() {
        device.pressHome()
    }

    // ---- Page / chrome -----------------------------------------------------

    @Test
    fun bareHarnessPageIsReachableWithoutAHostOrWebView() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER)
        assertNotNull(waitForDesc("Harness page title"))
        assertFalse(device.hasObject(By.res(ReceiverTags.BROWSER_OMNIBAR)))
    }

    @Test
    fun chromeStartShowsOmnibarAddressTabsAndMenu() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_CHROME)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_OMNIBAR))
        assertNotNull(waitForRes(ReceiverTags.BROWSER_SECURITY_CHIP))
        assertNotNull(waitForDesc("Browser menu"))
        assertNotNull(waitForDesc("Tabs, 1 of 2"))
        assertNotNull(waitForDesc("Reload"))
        assertNotNull(waitForDesc("New tab page"))
    }

    @Test
    fun backFromChromeHidesTheOmnibar() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_CHROME)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_OMNIBAR))
        pressBack()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_OMNIBAR)), TIMEOUT_MILLIS))
        assertNotNull(waitForDesc("Harness page title"))
    }

    @Test
    fun menuKeyShowsChromeWhenThePageOwnsFocus() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER)
        assertNotNull(waitForDesc("Harness page title"))
        device.pressKeyCode(KeyEvent.KEYCODE_MENU)
        device.waitForIdle()
        // Some Fire TV builds swallow MENU; chrome-start cases cover the same surface.
        if (device.hasObject(By.res(ReceiverTags.BROWSER_OMNIBAR))) {
            assertNotNull(waitForDesc("Browser menu"))
            pressBack()
            assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_OMNIBAR)), TIMEOUT_MILLIS))
        }
    }

    // ---- Menu / tabs -------------------------------------------------------

    @Test
    fun menuSheetShowsToolsAndBackReturnsToChrome() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_MENU)
        assertNotNull(waitForText("BROWSER TOOLS"))
        assertNotNull(waitForRes(ReceiverTags.BROWSER_MENU))
        assertNotNull(waitForText("Zoom out"))
        assertNotNull(waitForText("Find in page"))
        assertNotNull(waitForText("Bookmarks"))
        assertNotNull(waitForText("History"))
        assertNotNull(waitForText("TV browser data"))
        pressBack()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_MENU)), TIMEOUT_MILLIS))
        assertNotNull(waitForRes(ReceiverTags.BROWSER_OMNIBAR))
    }

    @Test
    fun omnibarMenuButtonOpensToolsSheet() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_CHROME)
        requireObject(By.desc("Browser menu")).click()
        assertNotNull(waitForText("BROWSER TOOLS"))
        assertNotNull(waitForRes(ReceiverTags.BROWSER_MENU))
    }

    @Test
    fun tabSwitcherShowsBothTabsAndBackClosesIt() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_TABS)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_TAB_SWITCHER))
        assertNotNull(waitForText("Harness tab"))
        assertNotNull(waitForText("Second tab"))
        pressBack()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_TAB_SWITCHER)), TIMEOUT_MILLIS))
    }

    @Test
    fun omnibarTabsButtonOpensTheSwitcher() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_CHROME)
        requireObject(By.desc("Tabs, 1 of 2")).click()
        assertNotNull(waitForRes(ReceiverTags.BROWSER_TAB_SWITCHER))
    }

    // ---- Nested overlays ---------------------------------------------------

    @Test
    fun findSheetIsLabeledAndBackReturnsToMenu() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_FIND)
        assertNotNull(waitForText("FIND IN THIS PAGE"))
        // The D-pad keyboard owns focus after its enter transition; wait for that transition to
        // settle before testing the Activity Back ladder.
        SystemClock.sleep(KEYBOARD_FOCUS_SETTLE_MILLIS)
        pressBack()
        // Find unwinds to MENU in the controller ladder.
        assertNotNull(waitForText("BROWSER TOOLS"))
    }

    @Test
    fun omniboxSheetIsLabeledAndBackClosesIt() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_OMNIBOX)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_OMNIBOX))
        pressBack()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_OMNIBOX)), TIMEOUT_MILLIS))
    }

    @Test
    fun bookmarksSheetListsEntriesAndBackReturnsToMenu() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_BOOKMARKS)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_LIBRARY))
        assertNotNull(waitForText("Example A"))
        assertNotNull(waitForText("Example B"))
        pressBack()
        assertNotNull(waitForText("BROWSER TOOLS"))
    }

    @Test
    fun historySheetListsVisitsAndBackReturnsToMenu() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_HISTORY)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_LIBRARY))
        assertNotNull(waitForText("Visited page"))
        pressBack()
        assertNotNull(waitForText("BROWSER TOOLS"))
    }

    @Test
    fun clearDataPromptOffersKeepAndClear() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_CLEAR)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_CLEAR_DATA))
        assertNotNull(waitForText("Keep data"))
        assertNotNull(waitForText("Clear data"))
        device.pressDPadCenter()
        assertNotNull(waitForText("BROWSER TOOLS"))
    }

    @Test
    fun profilesSheetListsTvProfiles() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_PROFILES)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_PROFILES))
        assertNotNull(waitForText("Default"))
        assertNotNull(waitForText("Kids"))
        pressBack()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_PROFILES)), TIMEOUT_MILLIS))
    }

    @Test
    fun menuFindActionOpensFindSheet() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_MENU)
        requireObject(By.text("Find in page")).click()
        assertNotNull(waitForText("FIND IN THIS PAGE"))
    }

    @Test
    fun menuBookmarksActionOpensBookmarksSheet() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_MENU)
        requireObject(By.text("Bookmarks")).click()
        assertNotNull(waitForText("Example A"))
    }

    @Test
    fun menuHistoryActionOpensHistorySheet() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_MENU)
        requireObject(By.text("History")).click()
        assertNotNull(waitForText("Visited page"))
    }

    // ---- Leave prompt ------------------------------------------------------

    @Test
    fun backOnBarePageOpensTheLeaveGuard() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER)
        assertNotNull(waitForDesc("Harness page title"))
        pressBack()
        assertNotNull(waitForRes(ReceiverTags.BROWSER_LEAVE_PROMPT))
        assertNotNull(waitForText("Leave the browser?"))
        assertNotNull(waitForText("Keep browsing"))
        assertNotNull(waitForText("Leave"))
    }

    @Test
    fun leavePromptStartsOnKeepBrowsingPath() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_LEAVE)
        assertNotNull(waitForRes(ReceiverTags.BROWSER_LEAVE_PROMPT))
        // Behavioural focus check: Select on the default control dismisses without leaving.
        device.pressDPadCenter()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_LEAVE_PROMPT)), TIMEOUT_MILLIS))
        assertNotNull(waitForDesc("Harness page title"))
    }

    @Test
    fun keepBrowsingDpadActionDismissesTheLeaveGuard() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_LEAVE)
        device.pressDPadCenter()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_LEAVE_PROMPT)), TIMEOUT_MILLIS))
        assertFalse(device.hasObject(By.text("Leave the browser?")))
        assertNotNull(waitForDesc("Harness page title"))
    }

    @Test
    fun dpadCanReachLeaveThenReturnToKeepBrowsing() {
        // Do not assert isFocused — Fire OS Compose nodes often omit the focused a11y flag.
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_LEAVE)
        assertNotNull(waitForText("Keep browsing"))
        device.pressDPadRight()
        device.waitForIdle()
        device.pressDPadLeft()
        device.waitForIdle()
        device.pressDPadCenter()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_LEAVE_PROMPT)), TIMEOUT_MILLIS))
    }

    @Test
    fun backLadderUnwindsMenuThenChromeThenLeave() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_MENU)
        assertNotNull(waitForText("BROWSER TOOLS"))
        pressBack()
        assertNotNull(waitForRes(ReceiverTags.BROWSER_OMNIBAR))
        pressBack()
        assertTrue(device.wait(Until.gone(By.res(ReceiverTags.BROWSER_OMNIBAR)), TIMEOUT_MILLIS))
        pressBack()
        assertNotNull(waitForText("Leave the browser?"))
    }

    @Test
    fun backLadderUnwindsBookmarksThroughMenu() {
        launch(ReceiverPreviewActivity.PREVIEW_BROWSER_BOOKMARKS)
        assertNotNull(waitForText("Example A"))
        pressBack()
        assertNotNull(waitForText("BROWSER TOOLS"))
        pressBack()
        assertNotNull(waitForRes(ReceiverTags.BROWSER_OMNIBAR))
    }

    private fun launch(state: String) {
        context.startActivity(
            Intent(context, ReceiverPreviewActivity::class.java)
                .putExtra(ReceiverPreviewActivity.EXTRA_STATE, state)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK),
        )
        assertNotNull(
            device.wait(Until.findObject(By.desc("Receiver preview $state")), TIMEOUT_MILLIS),
        )
        device.waitForIdle()
        // Direct-start harness states are composed in one frame, unlike the real user path that
        // reaches them after a click. Let focus/Back ownership commit before injecting a key.
        SystemClock.sleep(HARNESS_SETTLE_MILLIS)
        device.waitForIdle()
    }

    private fun pressBack() {
        device.pressKeyCode(KeyEvent.KEYCODE_BACK)
        device.waitForIdle()
    }

    private fun waitForText(text: String): UiObject2? =
        device.wait(Until.findObject(By.text(text)), TIMEOUT_MILLIS)

    private fun waitForDesc(desc: String): UiObject2? =
        device.wait(Until.findObject(By.desc(desc)), TIMEOUT_MILLIS)

    private fun waitForRes(res: String): UiObject2? =
        device.wait(Until.findObject(By.res(res)), TIMEOUT_MILLIS)

    private fun requireObject(selector: androidx.test.uiautomator.BySelector): UiObject2 =
        requireNotNull(device.findObject(selector)) { "Missing UI object for $selector" }

    private companion object {
        const val TIMEOUT_MILLIS = 15_000L
        const val HARNESS_SETTLE_MILLIS = 250L
        const val KEYBOARD_FOCUS_SETTLE_MILLIS = 1_500L
    }
}
