package com.rextechnologies.flint.receiver.ui

import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.receiver.browser.BrowserCursorEngine
import com.rextechnologies.flint.receiver.browser.BrowserInteractionMode
import com.rextechnologies.flint.receiver.browser.BrowserKey
import com.rextechnologies.flint.receiver.browser.BrowserKeyboard
import com.rextechnologies.flint.receiver.browser.BrowserKeyboardState
import com.rextechnologies.flint.receiver.browser.BrowserLibraryProfile
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import com.rextechnologies.flint.receiver.browser.BrowserNativeKey
import com.rextechnologies.flint.receiver.browser.BrowserOverlay
import com.rextechnologies.flint.receiver.browser.BrowserQueryResolver
import com.rextechnologies.flint.receiver.browser.BrowserSearchEngine
import com.rextechnologies.flint.receiver.browser.BrowserSemanticKey
import com.rextechnologies.flint.receiver.browser.BrowserTvInputModel
import com.rextechnologies.flint.receiver.browser.BrowserTvState
import com.rextechnologies.flint.receiver.browser.CursorDirection
import com.rextechnologies.flint.receiver.browser.CursorState
import com.rextechnologies.flint.receiver.browser.CursorViewport
import com.rextechnologies.flint.receiver.browser.ResolvedQuery
import com.rextechnologies.flint.receiver.browser.TvKeyOutcome
import com.rextechnologies.flint.receiver.browser.toNativeKey

/** What the surface asks of the rest of the receiver. */
internal interface BrowserSurfaceActions {
    fun dispatch(input: BrowserNativeInput)
    fun viewport(): Pair<Int, Int>?
    fun goBack()
    fun goForward()
    fun reload()
    fun stopLoading()
    fun navigate(url: String)
    fun openTab()
    fun selectTab(tabId: Long)
    fun closeTab(tabId: Long)
    fun closeActiveTab()
    fun startFind(query: String)
    fun findNext()
    fun findPrevious()
    fun clearFind()
    fun selectTvProfile(profileId: String)
    fun selectConnectedDeviceProfile()
    fun createTvProfile(name: String)
    fun renameTvProfile(profileId: String, name: String)
    fun deleteTvProfile(profileId: String)
    fun closeBrowser()
    fun dismissNotice()
    fun cancelDialog()
    fun exitFullscreen()
    fun notice(message: String)
}

/**
 * The surface's own state machine: interaction mode, chrome, overlays, the cursor and the keyboard.
 *
 * Kept out of the composable so the wiring is readable and so the composable stays a rendering of
 * state rather than a place decisions are made. Every decision it takes comes from a pure model that
 * is already tested — [BrowserTvInputModel] for what a key means, [BrowserCursorEngine] for where
 * the pointer goes, [BrowserKeyboard] for typing, [BrowserQueryResolver] for address-or-search.
 * This class only carries the result across the Android boundary.
 */
@Stable
internal class BrowserSurfaceController(
    private val actions: BrowserSurfaceActions,
    private val keys: BrowserTvInputModel = BrowserTvInputModel(),
    private val cursorEngine: BrowserCursorEngine = BrowserCursorEngine(),
    private val keyboard: BrowserKeyboard = BrowserKeyboard(),
    private val queries: BrowserQueryResolver = BrowserQueryResolver(),
) {
    var mode by mutableStateOf(BrowserInteractionMode.CURSOR)
        private set

    var chromeVisible by mutableStateOf(false)
        private set

    var chromeFocused by mutableStateOf(false)

    var overlay by mutableStateOf(BrowserOverlay.NONE)
        private set

    var cursor by mutableStateOf(CursorState())
        private set

    var cursorPressed by mutableStateOf(false)
        private set

    var keyboardState by mutableStateOf(BrowserKeyboardState())
        private set

    var leaveConfirmVisible by mutableStateOf(false)
        private set

    var searchEngine by mutableStateOf(BrowserSearchEngine.DEFAULT)

    /** Set by the page's own callbacks; the cursor and chrome stand down while a video owns the screen. */
    var fullscreen by mutableStateOf(false)

    var dialogOpen by mutableStateOf(false)

    /**
     * Whether a page text field has focus.
     *
     * Set from the WebView's own input-connection request, so it is true exactly while Fire OS has
     * its keyboard up over an editable element.
     */
    var editingFocused by mutableStateOf(false)

    var noticeVisible by mutableStateOf(false)

    var canGoBack by mutableStateOf(false)

    var tabCount by mutableStateOf(1)

    var pageLoading by mutableStateOf(false)

    var editingProfileId by mutableStateOf<String?>(null)
        private set

    var deletingProfile by mutableStateOf<BrowserLibraryProfile?>(null)
        private set

    /**
     * When the workspace overlay is open, Back asks this first. Return true to keep the overlay
     * (handled a ladder rung); false to dismiss the workspace.
     */
    var workspaceBackHandler: (() -> Boolean)? = null

    private var heldDirection: CursorDirection? = null
    private var backPressed = false
    private var focusSelectPending = false
    private var longPressConsumed = false

    fun onKeyDown(keyCode: Int): Boolean {
        if (keyCode == android.view.KeyEvent.KEYCODE_BACK) {
            backPressed = true
            longPressConsumed = false
            return true
        }
        // Only defer Select for page focus-mode clicks. While chrome owns the remote, Compose must
        // see a normal press — holding Select here used to toggle pointer/link mode instead.
        if (isSelectKey(keyCode) && mode == BrowserInteractionMode.FOCUS &&
            overlay == BrowserOverlay.NONE && !dialogOpen && !fullscreen &&
            !leaveConfirmVisible &&
            !(chromeVisible && chromeFocused)
        ) {
            focusSelectPending = true
            longPressConsumed = false
            return true
        }
        if (isSelectKey(keyCode)) longPressConsumed = false
        val outcome = keys.onKeyDown(snapshot(), keyCode)
        if ((
                overlay == BrowserOverlay.OMNIBOX ||
                    overlay == BrowserOverlay.FIND ||
                    overlay == BrowserOverlay.PROFILE_NAME
                ) &&
            outcome is TvKeyOutcome.PassThrough
        ) {
            return onOmniboxKey(keyCode)
        }
        return apply(outcome)
    }

    fun onKeyUp(keyCode: Int): Boolean {
        if (keyCode == android.view.KeyEvent.KEYCODE_BACK && backPressed) {
            backPressed = false
            val consumed = longPressConsumed
            longPressConsumed = false
            if (!consumed) apply(keys.onKeyDown(snapshot(), keyCode))
            return true
        }
        if (isSelectKey(keyCode) && focusSelectPending) {
            focusSelectPending = false
            val consumed = longPressConsumed
            longPressConsumed = false
            if (!consumed) apply(keys.onKeyDown(snapshot(), keyCode))
            return true
        }
        if (heldDirection != null && isDirectionKey(keyCode)) {
            heldDirection = null
            cursor = cursorEngine.release(cursor)
            return true
        }
        if (cursorPressed && isSelectKey(keyCode)) {
            releaseCursorClick()
            return true
        }
        return false
    }

    fun onKeyLongPress(keyCode: Int): Boolean {
        if (keyCode == android.view.KeyEvent.KEYCODE_BACK || isSelectKey(keyCode)) {
            longPressConsumed = true
        }
        if (cursorPressed && isSelectKey(keyCode)) {
            cancelCursorClick()
        }
        focusSelectPending = false
        return apply(keys.onKeyLongPress(snapshot(), keyCode))
    }

    /** One frame of a held direction. Driven by the surface's repeat loop. */
    fun onRepeatFrame(elapsedMillis: Long) {
        if (heldDirection == null ||
            overlay != BrowserOverlay.NONE ||
            leaveConfirmVisible ||
            dialogOpen
        ) {
            return
        }
        val step = cursorEngine.hold(cursor, elapsedMillis, viewport())
        cursor = step.state
        publishCursorHover()
        if (step.scrollX != 0 || step.scrollY != 0) {
            actions.dispatch(
                BrowserNativeInput.Scroll(cursor.x, cursor.y, step.scrollX, step.scrollY),
            )
        }
    }

    fun showChrome() {
        chromeVisible = true
    }

    fun hideChrome() {
        chromeVisible = false
        chromeFocused = false
    }

    fun openOverlay(next: BrowserOverlay) {
        overlay = next
        if (next == BrowserOverlay.OMNIBOX || next == BrowserOverlay.FIND) {
            keyboardState = BrowserKeyboardState()
        }
    }

    fun closeOverlay() {
        if (overlay == BrowserOverlay.WORKSPACE && workspaceBackHandler?.invoke() == true) {
            return
        }
        overlay = when (overlay) {
            BrowserOverlay.FIND,
            BrowserOverlay.BOOKMARKS,
            BrowserOverlay.HISTORY,
            BrowserOverlay.CLEAR_DATA,
            BrowserOverlay.NETWORK,
            -> BrowserOverlay.MENU

            BrowserOverlay.PROFILE_NAME -> {
                editingProfileId = null
                keyboardState = BrowserKeyboardState()
                BrowserOverlay.PROFILES
            }

            BrowserOverlay.PROFILE_DELETE -> {
                deletingProfile = null
                BrowserOverlay.PROFILES
            }

            BrowserOverlay.NONE,
            BrowserOverlay.OMNIBOX,
            BrowserOverlay.TABS,
            BrowserOverlay.MENU,
            BrowserOverlay.PROFILES,
            BrowserOverlay.WORKSPACE,
            -> BrowserOverlay.NONE
        }
    }

    fun dismissOverlays() {
        workspaceBackHandler = null
        overlay = BrowserOverlay.NONE
        editingProfileId = null
        deletingProfile = null
    }

    fun onOmnibarAction(action: OmnibarAction) {
        when (action) {
            OmnibarAction.BACK -> actions.goBack()
            OmnibarAction.FORWARD -> actions.goForward()
            OmnibarAction.RELOAD_OR_STOP -> if (pageLoading) actions.stopLoading() else actions.reload()
            OmnibarAction.HOME -> openOverlay(BrowserOverlay.OMNIBOX)
            OmnibarAction.ADDRESS -> openOverlay(BrowserOverlay.OMNIBOX)
            OmnibarAction.TABS -> openOverlay(BrowserOverlay.TABS)
            OmnibarAction.WORKSPACE -> openOverlay(BrowserOverlay.WORKSPACE)
            OmnibarAction.MENU -> openOverlay(BrowserOverlay.MENU)
        }
    }

    fun newTab() {
        closeOverlay()
        actions.openTab()
    }

    fun selectTab(tabId: Long) {
        closeOverlay()
        actions.selectTab(tabId)
    }

    fun closeTab(tabId: Long) {
        actions.closeTab(tabId)
    }

    fun chooseTvProfile(profileId: String) {
        actions.selectTvProfile(profileId)
        closeOverlay()
    }

    fun chooseConnectedDeviceProfile() {
        actions.selectConnectedDeviceProfile()
        closeOverlay()
    }

    fun beginCreateProfile() {
        editingProfileId = null
        keyboardState = BrowserKeyboardState()
        openOverlay(BrowserOverlay.PROFILE_NAME)
    }

    fun beginRenameProfile(profile: BrowserLibraryProfile) {
        editingProfileId = profile.id
        keyboardState = BrowserKeyboardState(text = profile.name)
        openOverlay(BrowserOverlay.PROFILE_NAME)
    }

    fun saveProfileName() {
        val name = keyboard.submit(keyboardState)
        if (name.isEmpty()) {
            actions.notice("A TV profile needs a name.")
            return
        }
        editingProfileId?.let { profileId ->
            actions.renameTvProfile(profileId, name)
        } ?: actions.createTvProfile(name)
        keyboardState = BrowserKeyboardState()
        editingProfileId = null
        openOverlay(BrowserOverlay.PROFILES)
    }

    fun requestDeleteProfile(profile: BrowserLibraryProfile) {
        deletingProfile = profile
        openOverlay(BrowserOverlay.PROFILE_DELETE)
    }

    fun cancelDeleteProfile() {
        deletingProfile = null
        openOverlay(BrowserOverlay.PROFILES)
    }

    fun confirmDeleteProfile() {
        deletingProfile?.let { actions.deleteTvProfile(it.id) }
        deletingProfile = null
        openOverlay(BrowserOverlay.PROFILES)
    }

    /** Places the cursor when the page is first measured, or after it changes size. */
    fun onViewportChanged() {
        val measured = viewport()
        cursor = if (cursor.x == 0 && cursor.y == 0) {
            cursorEngine.centre(measured)
        } else {
            cursorEngine.clampTo(cursor, measured)
        }
    }

    private fun apply(outcome: TvKeyOutcome): Boolean = when (outcome) {
        is TvKeyOutcome.MoveCursor -> {
            heldDirection = outcome.direction
            val step = cursorEngine.tap(cursor, outcome.direction, viewport())
            cursor = step.state
            publishCursorHover()
            if (step.scrollX != 0 || step.scrollY != 0) {
                actions.dispatch(
                    BrowserNativeInput.Scroll(cursor.x, cursor.y, step.scrollX, step.scrollY),
                )
            }
            true
        }

        TvKeyOutcome.ClickCursor -> {
            pressCursorClick()
            true
        }

        TvKeyOutcome.CommitText -> {
            actions.dispatch(BrowserNativeInput.KeyStroke(BrowserNativeKey.ENTER))
            true
        }

        is TvKeyOutcome.SendKey -> {
            actions.dispatch(
                BrowserNativeInput.KeyStroke(
                    outcome.key.toNativeKey(),
                    shift = outcome.key == BrowserSemanticKey.SHIFT_TAB,
                ),
            )
            true
        }

        is TvKeyOutcome.ScrollPage -> {
            actions.dispatch(
                BrowserNativeInput.Scroll(cursor.x, cursor.y, outcome.deltaX, outcome.deltaY),
            )
            true
        }

        TvKeyOutcome.ToggleMode -> {
            mode = if (mode == BrowserInteractionMode.CURSOR) {
                BrowserInteractionMode.FOCUS
            } else {
                BrowserInteractionMode.CURSOR
            }
            // Named rather than left to be discovered: the same button now does something else, and
            // silently changing what a remote means is how a browser feels broken.
            actions.notice(
                if (mode == BrowserInteractionMode.CURSOR) {
                    "Pointer mode — the arrows move a pointer"
                } else {
                    "Link mode — the arrows step between links"
                },
            )
            true
        }

        TvKeyOutcome.ShowChrome -> {
            showChrome()
            true
        }
        TvKeyOutcome.HideChrome -> {
            hideChrome()
            true
        }
        is TvKeyOutcome.OpenOverlay -> {
            openOverlay(outcome.overlay)
            true
        }
        TvKeyOutcome.CloseOverlay -> {
            closeOverlay()
            true
        }
        TvKeyOutcome.ExitFullscreen -> {
            actions.exitFullscreen()
            true
        }
        TvKeyOutcome.DismissNotice -> {
            actions.dismissNotice()
            true
        }
        TvKeyOutcome.CancelDialog -> {
            actions.cancelDialog()
            true
        }
        TvKeyOutcome.GoBack -> {
            actions.goBack()
            true
        }

        TvKeyOutcome.CloseTab -> {
            actions.closeActiveTab()
            true
        }

        TvKeyOutcome.ConfirmLeave -> {
            leaveConfirmVisible = true
            true
        }
        TvKeyOutcome.CloseBrowser -> {
            leaveConfirmVisible = false
            actions.closeBrowser()
            true
        }
        TvKeyOutcome.PassThrough -> false
    }

    fun cancelLeaveConfirm() {
        leaveConfirmVisible = false
    }

    /** Affirmative leave from the guard prompt — not a deferred Back key. */
    fun confirmLeave() {
        leaveConfirmVisible = false
        actions.closeBrowser()
    }

    /** Opens the leave guard without inventing a Back key (harness / tests). */
    fun showLeaveConfirm() {
        leaveConfirmVisible = true
    }

    private fun onOmniboxKey(keyCode: Int): Boolean {
        val direction = when (keyCode) {
            android.view.KeyEvent.KEYCODE_DPAD_UP -> CursorDirection.UP
            android.view.KeyEvent.KEYCODE_DPAD_DOWN -> CursorDirection.DOWN
            android.view.KeyEvent.KEYCODE_DPAD_LEFT -> CursorDirection.LEFT
            android.view.KeyEvent.KEYCODE_DPAD_RIGHT -> CursorDirection.RIGHT
            else -> null
        }
        if (direction != null) {
            keyboardState = keyboardState.copy(
                cursor = keyboard.move(keyboardState.cursor, direction, keyboardState.page),
            )
            return true
        }
        if (isSelectKey(keyCode)) {
            val key = keyboard.keyAt(keyboardState.cursor, keyboardState.page)
            if (key is BrowserKey.Submit) {
                submitOmnibox()
            } else {
                keyboardState = keyboard.press(keyboardState, key)
            }
            return true
        }
        return false
    }

    private fun submitOmnibox() {
        val typed = keyboard.submit(keyboardState)
        if (overlay == BrowserOverlay.PROFILE_NAME) {
            saveProfileName()
            return
        }
        if (overlay == BrowserOverlay.FIND) {
            dismissOverlays()
            if (typed.isNotBlank()) actions.startFind(typed)
            return
        }
        when (val resolved = queries.resolve(typed, searchEngine)) {
            is ResolvedQuery.Navigate -> {
                closeOverlay()
                hideChrome()
                actions.navigate(resolved.url)
            }
            is ResolvedQuery.Search -> {
                closeOverlay()
                hideChrome()
                actions.navigate(resolved.url)
            }
            ResolvedQuery.Empty -> closeOverlay()
        }
    }

    private fun pressCursorClick() {
        cursorPressed = true
        actions.dispatch(
            BrowserNativeInput.Pointer(BrowserPointerAction.DOWN, cursor.x, cursor.y, buttons = 1),
        )
    }

    private fun releaseCursorClick() {
        cursorPressed = false
        actions.dispatch(
            BrowserNativeInput.Pointer(BrowserPointerAction.UP, cursor.x, cursor.y, buttons = 0),
        )
    }

    private fun cancelCursorClick() {
        cursorPressed = false
        actions.dispatch(
            BrowserNativeInput.Pointer(BrowserPointerAction.CANCEL, cursor.x, cursor.y, buttons = 0),
        )
    }

    /**
     * Tells the page where the TV cursor is, as a mouse hover.
     *
     * Without this the Compose crosshair moves but YouTube (and anything else that waits on
     * `:hover` / `mouseover`) never sees a pointer — controls stay hidden until Select taps.
     */
    private fun publishCursorHover() {
        actions.dispatch(
            BrowserNativeInput.Pointer(
                BrowserPointerAction.MOVE,
                cursor.x,
                cursor.y,
                buttons = 0,
            ),
        )
    }

    private fun snapshot() = BrowserTvState(
        mode = mode,
        chromeVisible = chromeVisible,
        chromeFocused = chromeFocused,
        overlay = overlay,
        dialogOpen = dialogOpen,
        editingFocused = editingFocused,
        fullscreen = fullscreen,
        noticeVisible = noticeVisible,
        leaveConfirmVisible = leaveConfirmVisible,
        canGoBack = canGoBack,
        isLastTab = tabCount <= 1,
        cursorAtTopEdge = cursor.y <= BrowserCursorEngine.EDGE_MARGIN_PIXELS,
    )

    private fun viewport(): CursorViewport {
        val measured = actions.viewport() ?: return CursorViewport(0, 0)
        return CursorViewport(measured.first, measured.second)
    }

    private fun isDirectionKey(keyCode: Int) = keyCode == android.view.KeyEvent.KEYCODE_DPAD_UP ||
        keyCode == android.view.KeyEvent.KEYCODE_DPAD_DOWN ||
        keyCode == android.view.KeyEvent.KEYCODE_DPAD_LEFT ||
        keyCode == android.view.KeyEvent.KEYCODE_DPAD_RIGHT

    private fun isSelectKey(keyCode: Int) = keyCode == android.view.KeyEvent.KEYCODE_DPAD_CENTER ||
        keyCode == android.view.KeyEvent.KEYCODE_ENTER
}
