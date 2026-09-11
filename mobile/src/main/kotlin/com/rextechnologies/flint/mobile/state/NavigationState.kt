package com.rextechnologies.flint.mobile.state

import android.content.Context
import com.rextechnologies.flint.castcore.copy.MobileTab
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** Which page is showing, whether the introduction is done, and the one message on screen. */
data class Navigation(
    val tab: MobileTab = MobileTab.CAST,
    /** `null` until the stored answer has been read, so no screen flashes before it is known. */
    val introductionSeen: Boolean? = null,
    val pairingVisible: Boolean = false,
    /**
     * The pairing sheet's own error.
     *
     * Not a notice: a notice is a banner about the app, and this is a correction to what somebody
     * just typed, so it belongs beside the field they typed it into and goes when the sheet does.
     */
    val pairingError: String? = null,
    val notice: String? = null,
)

/**
 * Where the app is, and how back gets out of it.
 *
 * The introduction is remembered on disk. In memory it replayed all six steps on every cold start,
 * which is not an introduction — it is an obstacle between somebody and the thing they opened.
 */
class NavigationState(
    context: Context,
    private val scope: CoroutineScope,
) {
    private val preferences =
        context.applicationContext.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    private val mutable = MutableStateFlow(Navigation())
    val state: StateFlow<Navigation> = mutable

    init {
        // Read off the main thread. Until it arrives `introductionSeen` is null and the app shows
        // neither screen, which is a frame or two of the background colour rather than a wrong
        // screen that then swaps.
        scope.launch {
            val seen = withContext(Dispatchers.IO) { preferences.getBoolean(KEY_SEEN, false) }
            mutable.update { if (it.introductionSeen == null) it.copy(introductionSeen = seen) else it }
        }
    }

    fun selectTab(tab: MobileTab) {
        mutable.update { it.copy(tab = tab) }
    }

    /**
     * Handles a back press, and says whether it was used.
     *
     * `false` means let the system have it, which closes the app. Without this every back press from
     * every tab left the app, which is not what a bottom bar teaches people to expect.
     */
    fun onBack(): Boolean {
        val current = mutable.value
        return when {
            current.pairingVisible -> {
                showPairing(false)
                true
            }

            current.notice != null -> {
                dismissNotice()
                true
            }

            current.tab != MobileTab.CAST -> {
                selectTab(MobileTab.CAST)
                true
            }

            else -> false
        }
    }

    fun showPairing(visible: Boolean) {
        mutable.update { it.copy(pairingVisible = visible, pairingError = null) }
    }

    /** Rejects what was typed, without closing the sheet it was typed into. */
    fun pairingError(message: String?) {
        mutable.update { it.copy(pairingError = message) }
    }

    /** Says one thing, replacing whatever was being said. Two at once is one too many to read. */
    fun notice(message: String?) {
        mutable.update { it.copy(notice = message?.takeIf { text -> text.isNotBlank() }) }
    }

    fun dismissNotice() {
        mutable.update { it.copy(notice = null) }
    }

    fun markIntroductionSeen() {
        mutable.update { it.copy(introductionSeen = true) }
        // apply() updates the in-memory preferences immediately and schedules the disk write itself.
        // Wrapping it in another coroutine created a race where an immediate cold start could still
        // observe the old value even though this call had already returned.
        preferences.edit().putBoolean(KEY_SEEN, true).apply()
    }

    /**
     * Shows the introduction again, from Settings.
     *
     * Not persisted as unseen: somebody who asks to see it again has not forgotten it, and writing
     * "unseen" would mean it replayed on the next cold start too.
     */
    fun replayIntroduction() {
        mutable.update { it.copy(introductionSeen = false, tab = MobileTab.CAST) }
    }

    private companion object {
        const val PREFERENCES_NAME = "flint-mobile-navigation"
        const val KEY_SEEN = "introduction-seen"
    }
}
