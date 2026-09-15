package com.rextechnologies.flint.receiver.browser

import java.nio.charset.StandardCharsets

enum class BrowserDialogKind {
    ALERT,
    CONFIRM,
    PROMPT,
    BEFORE_UNLOAD,
}

/** Page or prompt text is intentionally printable only through an explicit UI adapter. */
class BrowserDialogText private constructor(
    val value: String,
    val utf8Bytes: Int,
) {
    companion object {
        const val MAX_UTF8_BYTES: Int = 4 * 1024

        fun fromPage(raw: String): BrowserDialogText = create(sanitize(raw))

        internal fun fromValidatedReply(raw: String): BrowserDialogText = create(raw)

        private fun create(value: String): BrowserDialogText {
            val bounded = truncateUtf8(value, MAX_UTF8_BYTES)
            return BrowserDialogText(
                value = bounded,
                utf8Bytes = bounded.toByteArray(StandardCharsets.UTF_8).size,
            )
        }

        private fun sanitize(raw: String): String {
            val result = StringBuilder(raw.length)
            var index = 0
            while (index < raw.length) {
                val character = raw[index]
                when {
                    Character.isHighSurrogate(character) -> {
                        if (index + 1 < raw.length && Character.isLowSurrogate(raw[index + 1])) {
                            result.append(character)
                            result.append(raw[index + 1])
                            index += 2
                        } else {
                            result.append('\uFFFD')
                            index += 1
                        }
                    }
                    Character.isLowSurrogate(character) -> {
                        result.append('\uFFFD')
                        index += 1
                    }
                    Character.isISOControl(character) -> {
                        result.append(' ')
                        index += 1
                    }
                    else -> {
                        result.append(character)
                        index += 1
                    }
                }
            }
            return result.toString().trim()
        }

        private fun truncateUtf8(value: String, maximumBytes: Int): String {
            if (value.toByteArray(StandardCharsets.UTF_8).size <= maximumBytes) return value
            val result = StringBuilder()
            var used = 0
            var index = 0
            while (index < value.length) {
                val point = value.codePointAt(index)
                val piece = String(Character.toChars(point))
                val bytes = piece.toByteArray(StandardCharsets.UTF_8).size
                if (used + bytes > maximumBytes) break
                result.append(piece)
                used += bytes
                index += Character.charCount(point)
            }
            return result.toString()
        }
    }

    override fun toString(): String = "BrowserDialogText(<redacted utf8Bytes=$utf8Bytes>)"
}

data class BrowserDialogRequest(
    val dialogId: Long,
    val epoch: Long,
    val kind: BrowserDialogKind,
    val origin: BrowserAddress,
    val message: BrowserDialogText,
    val defaultValue: BrowserDialogText? = null,
)

enum class BrowserDialogReplySource {
    TV_LOCAL,
    PINNED_SESSION,
}

sealed interface BrowserDialogAnswer {
    data object Confirm : BrowserDialogAnswer
    data object Cancel : BrowserDialogAnswer

    data class Prompt(val text: String) : BrowserDialogAnswer {
        override fun toString(): String = "BrowserDialogAnswer.Prompt(text=<redacted>)"
    }
}

sealed interface BrowserDialogEvent {
    data class Show(val request: BrowserDialogRequest, val nowMs: Long) : BrowserDialogEvent
    data class Reply(
        val epoch: Long,
        val dialogId: Long,
        val source: BrowserDialogReplySource,
        val answer: BrowserDialogAnswer,
    ) : BrowserDialogEvent
    data class Expire(val nowMs: Long) : BrowserDialogEvent
    data object Close : BrowserDialogEvent
}

data class BrowserActiveDialog(
    val request: BrowserDialogRequest,
    val shownAtMs: Long,
) {
    val origin: BrowserAddress get() = request.origin
}

data class BrowserDialogState(val active: BrowserActiveDialog? = null)

enum class BrowserDialogReplyRejection {
    INVALID_PROMPT,
    KIND_MISMATCH,
}

sealed interface BrowserDialogEffect {
    data class ShowLocal(val request: BrowserDialogRequest) : BrowserDialogEffect
    data object RejectPageDialog : BrowserDialogEffect
    data class RejectReply(val reason: BrowserDialogReplyRejection) : BrowserDialogEffect
    data class ResolvePageDialog(
        val accepted: Boolean,
        val text: BrowserDialogText? = null,
    ) : BrowserDialogEffect {
        override fun toString(): String =
            "BrowserDialogEffect.ResolvePageDialog(accepted=$accepted, text=${if (text == null) "null" else "<redacted>"})"
    }
}

data class BrowserDialogTransition(
    val state: BrowserDialogState,
    val effect: BrowserDialogEffect?,
)

/** Exactly-one dialog state with a single winner among local, pinned, timeout, and close replies. */
class BrowserDialogReducer {
    companion object {
        const val DEFAULT_TIMEOUT_MS: Long = 60_000
    }

    fun reduce(state: BrowserDialogState, event: BrowserDialogEvent): BrowserDialogTransition = when (event) {
        is BrowserDialogEvent.Show -> show(state, event)
        is BrowserDialogEvent.Reply -> reply(state, event)
        is BrowserDialogEvent.Expire -> expire(state, event)
        BrowserDialogEvent.Close -> close(state)
    }

    private fun show(state: BrowserDialogState, event: BrowserDialogEvent.Show): BrowserDialogTransition {
        if (state.active != null || event.request.epoch <= 0 || event.request.dialogId <= 0 || event.nowMs < 0) {
            return BrowserDialogTransition(state, BrowserDialogEffect.RejectPageDialog)
        }
        val active = BrowserActiveDialog(event.request, event.nowMs)
        return BrowserDialogTransition(BrowserDialogState(active), BrowserDialogEffect.ShowLocal(event.request))
    }

    private fun reply(state: BrowserDialogState, event: BrowserDialogEvent.Reply): BrowserDialogTransition {
        val active = state.active ?: return BrowserDialogTransition(state, null)
        if (active.request.epoch != event.epoch || active.request.dialogId != event.dialogId) {
            return BrowserDialogTransition(state, null)
        }

        val resolution = when (val answer = event.answer) {
            BrowserDialogAnswer.Confirm -> confirmResolution(active.request.kind)
            BrowserDialogAnswer.Cancel -> DialogResolution(accepted = false)
            is BrowserDialogAnswer.Prompt -> promptResolution(active.request.kind, answer.text)
        }
        if (resolution.rejection != null) {
            return BrowserDialogTransition(state, BrowserDialogEffect.RejectReply(resolution.rejection))
        }
        return BrowserDialogTransition(
            BrowserDialogState(),
            BrowserDialogEffect.ResolvePageDialog(resolution.accepted, resolution.text),
        )
    }

    private fun expire(state: BrowserDialogState, event: BrowserDialogEvent.Expire): BrowserDialogTransition {
        val active = state.active ?: return BrowserDialogTransition(state, null)
        val deadline = if (active.shownAtMs > Long.MAX_VALUE - DEFAULT_TIMEOUT_MS) {
            Long.MAX_VALUE
        } else {
            active.shownAtMs + DEFAULT_TIMEOUT_MS
        }
        if (event.nowMs < deadline) return BrowserDialogTransition(state, null)
        return BrowserDialogTransition(BrowserDialogState(), BrowserDialogEffect.ResolvePageDialog(accepted = false))
    }

    private fun close(state: BrowserDialogState): BrowserDialogTransition {
        if (state.active == null) return BrowserDialogTransition(state, null)
        return BrowserDialogTransition(BrowserDialogState(), BrowserDialogEffect.ResolvePageDialog(accepted = false))
    }

    private fun confirmResolution(kind: BrowserDialogKind): DialogResolution = when (kind) {
        BrowserDialogKind.BEFORE_UNLOAD -> DialogResolution(accepted = false)
        else -> DialogResolution(accepted = true)
    }

    private fun promptResolution(kind: BrowserDialogKind, rawText: String): DialogResolution {
        if (kind != BrowserDialogKind.PROMPT) {
            return DialogResolution(rejection = BrowserDialogReplyRejection.KIND_MISMATCH)
        }
        return when (val text = BrowserTextPolicy.validate(rawText)) {
            is BrowserTextValidation.Accepted -> DialogResolution(
                accepted = true,
                text = BrowserDialogText.fromValidatedReply(text.text.value),
            )
            is BrowserTextValidation.Rejected -> DialogResolution(
                rejection = BrowserDialogReplyRejection.INVALID_PROMPT,
            )
        }
    }

    private data class DialogResolution(
        val accepted: Boolean = false,
        val text: BrowserDialogText? = null,
        val rejection: BrowserDialogReplyRejection? = null,
    )
}
