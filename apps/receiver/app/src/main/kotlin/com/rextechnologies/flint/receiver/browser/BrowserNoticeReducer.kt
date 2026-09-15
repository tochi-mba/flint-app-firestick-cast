package com.rextechnologies.flint.receiver.browser

/** One short message on screen, with an identity so the surface can animate a replacement. */
data class BrowserNotice(
    val id: Long,
    val message: String,
    val shownAtMs: Long,
)

data class BrowserNoticeState(val active: BrowserNotice? = null)

/**
 * The browser's way of saying "that was refused, and here is why".
 *
 * Policy already denied popups, uploads, downloads, permissions and non-https links; what was
 * missing is that the viewer was never told. A link that does nothing at all is indistinguishable
 * from a broken browser, and on a television there is no console to check.
 *
 * Capacity one, newest wins. A hostile page can fire blocked requests in a loop, and that must cost
 * one line of chrome rather than an unbounded queue.
 */
class BrowserNoticeReducer {
    companion object {
        /** Long enough to read from a sofa, short enough not to sit over the page. */
        const val VISIBLE_MILLIS: Long = 4_000
    }

    private var nextId: Long = 1

    fun show(state: BrowserNoticeState, refusal: BrowserRefusal, nowMs: Long): BrowserNoticeState =
        show(state, refusal.viewerSentence, nowMs)

    fun show(state: BrowserNoticeState, message: String, nowMs: Long): BrowserNoticeState {
        val active = state.active
        // A page retrying the same blocked request must not restart the notice, or it flashes on
        // every attempt and never reaches its own expiry.
        if (active != null && active.message == message) {
            return state
        }
        val notice = BrowserNotice(id = nextId, message = message, shownAtMs = nowMs)
        nextId += 1
        return BrowserNoticeState(notice)
    }

    fun expire(state: BrowserNoticeState, nowMs: Long): BrowserNoticeState {
        val active = state.active ?: return state
        val deadline = if (active.shownAtMs > Long.MAX_VALUE - VISIBLE_MILLIS) {
            Long.MAX_VALUE
        } else {
            active.shownAtMs + VISIBLE_MILLIS
        }
        return if (nowMs >= deadline) BrowserNoticeState() else state
    }

    fun dismiss(state: BrowserNoticeState): BrowserNoticeState =
        if (state.active == null) state else BrowserNoticeState()
}
