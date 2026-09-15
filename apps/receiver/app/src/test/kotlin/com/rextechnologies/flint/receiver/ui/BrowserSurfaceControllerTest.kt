package com.rextechnologies.flint.receiver.ui

import android.view.KeyEvent
import com.rextechnologies.flint.receiver.browser.BrowserLibraryProfile
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import com.rextechnologies.flint.receiver.browser.BrowserOverlay
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class BrowserSurfaceControllerTest {
    @Test
    fun `reload-stop button stops an active load and reloads an idle page`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)

        controller.pageLoading = true
        controller.onOmnibarAction(OmnibarAction.RELOAD_OR_STOP)
        controller.pageLoading = false
        controller.onOmnibarAction(OmnibarAction.RELOAD_OR_STOP)

        assertEquals(listOf("stop", "reload"), actions.calls)
    }

    @Test
    fun `back with another tab closes only the foreground tab`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.tabCount = 2

        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))

        assertEquals(listOf("close-active-tab"), actions.calls)
        assertFalse(controller.leaveConfirmVisible)
    }

    @Test
    fun `short back closes tabs and menu overlays before touching history`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.canGoBack = true

        controller.openOverlay(BrowserOverlay.TABS)
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))
        assertEquals(BrowserOverlay.NONE, controller.overlay)

        controller.openOverlay(BrowserOverlay.MENU)
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))
        assertEquals(BrowserOverlay.NONE, controller.overlay)
        assertTrue(actions.calls.isEmpty())
    }

    @Test
    fun `short back hides chrome after overlays are already closed`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.showChrome()
        controller.chromeFocused = true

        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))

        assertFalse(controller.chromeVisible)
        assertTrue(actions.calls.isEmpty())
    }

    @Test
    fun `back on the sole tab opens the leave guard before closing anything`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.tabCount = 1

        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))

        assertTrue(controller.leaveConfirmVisible)
        assertTrue(actions.calls.isEmpty())
    }

    @Test
    fun `keep browsing dismisses the leave guard without closing the session`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.tabCount = 1
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.leaveConfirmVisible)

        controller.cancelLeaveConfirm()

        assertFalse(controller.leaveConfirmVisible)
        assertTrue(actions.calls.isEmpty())
    }

    @Test
    fun `leave from the guard closes the browser session`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.tabCount = 1
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))

        controller.confirmLeave()

        assertFalse(controller.leaveConfirmVisible)
        assertEquals(listOf("close-browser"), actions.calls)
    }

    @Test
    fun `d-pad while the leave guard is up does not move the page cursor`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.tabCount = 1
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_BACK))
        assertTrue(controller.onKeyUp(KeyEvent.KEYCODE_BACK))
        val before = controller.cursor

        assertFalse(controller.onKeyDown(KeyEvent.KEYCODE_DPAD_DOWN))
        assertFalse(controller.onKeyDown(KeyEvent.KEYCODE_DPAD_CENTER))

        assertEquals(before, controller.cursor)
        assertTrue(controller.leaveConfirmVisible)
        assertTrue(actions.calls.isEmpty())
    }

    @Test
    fun `tab selection closes the switcher before changing renderer`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.openOverlay(BrowserOverlay.TABS)

        controller.selectTab(8)

        assertEquals(BrowserOverlay.NONE, controller.overlay)
        assertEquals(listOf("select:8"), actions.calls)
    }

    @Test
    fun `new tab closes the switcher and asks the tab layer for a blank page`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.openOverlay(BrowserOverlay.TABS)

        controller.newTab()

        assertEquals(BrowserOverlay.NONE, controller.overlay)
        assertEquals(listOf("open-tab"), actions.calls)
    }

    @Test
    fun `profile names can be entered and saved using only dpad keys`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.beginCreateProfile()

        // The keyboard starts on q. Up wraps to its action row, then six rights land on Save.
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_DPAD_CENTER))
        assertEquals("q", controller.keyboardState.text)
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_DPAD_UP))
        repeat(6) { assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_DPAD_RIGHT)) }
        assertTrue(controller.onKeyDown(KeyEvent.KEYCODE_DPAD_CENTER))

        assertEquals(BrowserOverlay.PROFILES, controller.overlay)
        assertEquals(listOf("create-profile:q"), actions.calls)
    }

    @Test
    fun `rename and guarded delete return to the profile chooser`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        val profile = BrowserLibraryProfile("family", "Family")

        controller.beginRenameProfile(profile)
        assertEquals("Family", controller.keyboardState.text)
        controller.saveProfileName()
        controller.requestDeleteProfile(profile)
        assertEquals(BrowserOverlay.PROFILE_DELETE, controller.overlay)
        controller.confirmDeleteProfile()

        assertEquals(BrowserOverlay.PROFILES, controller.overlay)
        assertEquals(listOf("rename-profile:family:Family", "delete-profile:family"), actions.calls)
    }

    @Test
    fun `choosing a TV or connected-device profile closes the chooser`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)

        controller.openOverlay(BrowserOverlay.PROFILES)
        controller.chooseTvProfile("kids")
        assertEquals(BrowserOverlay.NONE, controller.overlay)
        controller.openOverlay(BrowserOverlay.PROFILES)
        controller.chooseConnectedDeviceProfile()

        assertEquals(BrowserOverlay.NONE, controller.overlay)
        assertEquals(listOf("select-tv-profile:kids", "select-device-profile"), actions.calls)
    }

    @Test
    fun `back unwinds profile editing to profiles and library sheets to the menu`() {
        val controller = BrowserSurfaceController(RecordingActions())

        controller.beginCreateProfile()
        controller.closeOverlay()
        assertEquals(BrowserOverlay.PROFILES, controller.overlay)
        controller.closeOverlay()
        assertEquals(BrowserOverlay.NONE, controller.overlay)

        controller.openOverlay(BrowserOverlay.BOOKMARKS)
        controller.closeOverlay()
        assertEquals(BrowserOverlay.MENU, controller.overlay)
    }

    @Test
    fun `omnibar menu select opens the menu on a normal press path`() {
        val actions = RecordingActions()
        val controller = BrowserSurfaceController(actions)
        controller.showChrome()

        controller.onOmnibarAction(OmnibarAction.MENU)

        assertEquals(BrowserOverlay.MENU, controller.overlay)
    }

    @Test
    fun `omnibar tabs select opens the tab switcher on a normal press path`() {
        val controller = BrowserSurfaceController(RecordingActions())
        controller.showChrome()

        controller.onOmnibarAction(OmnibarAction.TABS)

        assertEquals(BrowserOverlay.TABS, controller.overlay)
    }

    @Test
    fun `omnibar mosaic select opens the video mosaic`() {
        val controller = BrowserSurfaceController(RecordingActions())
        controller.showChrome()

        controller.onOmnibarAction(OmnibarAction.WORKSPACE)

        assertEquals(BrowserOverlay.WORKSPACE, controller.overlay)
    }

    private class RecordingActions : BrowserSurfaceActions {
        val calls = mutableListOf<String>()

        override fun dispatch(input: BrowserNativeInput) = Unit
        override fun viewport(): Pair<Int, Int> = 1280 to 720
        override fun goBack() {
            calls += "back"
        }
        override fun goForward() {
            calls += "forward"
        }
        override fun reload() {
            calls += "reload"
        }
        override fun stopLoading() {
            calls += "stop"
        }
        override fun navigate(url: String) {
            calls += "navigate:$url"
        }
        override fun openTab() {
            calls += "open-tab"
        }
        override fun selectTab(tabId: Long) {
            calls += "select:$tabId"
        }
        override fun closeTab(tabId: Long) {
            calls += "close:$tabId"
        }
        override fun closeActiveTab() {
            calls += "close-active-tab"
        }
        override fun startFind(query: String) {
            calls += "find:$query"
        }
        override fun findNext() {
            calls += "find-next"
        }
        override fun findPrevious() {
            calls += "find-previous"
        }
        override fun clearFind() {
            calls += "find-clear"
        }
        override fun selectTvProfile(profileId: String) {
            calls += "select-tv-profile:$profileId"
        }
        override fun selectConnectedDeviceProfile() {
            calls += "select-device-profile"
        }
        override fun createTvProfile(name: String) {
            calls += "create-profile:$name"
        }
        override fun renameTvProfile(profileId: String, name: String) {
            calls += "rename-profile:$profileId:$name"
        }
        override fun deleteTvProfile(profileId: String) {
            calls += "delete-profile:$profileId"
        }
        override fun closeBrowser() {
            calls += "close-browser"
        }
        override fun dismissNotice() {
            calls += "dismiss-notice"
        }
        override fun cancelDialog() {
            calls += "cancel-dialog"
        }
        override fun exitFullscreen() {
            calls += "exit-fullscreen"
        }
        override fun notice(message: String) {
            calls += "notice:$message"
        }
    }
}
