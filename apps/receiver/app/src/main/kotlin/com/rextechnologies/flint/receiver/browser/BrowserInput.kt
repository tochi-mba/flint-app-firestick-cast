package com.rextechnologies.flint.receiver.browser

import java.nio.charset.StandardCharsets
import com.rextechnologies.flint.protocol.wire.BrowserSemanticKey as WireBrowserSemanticKey

/** Re-exported only as a portable semantic enum; Android key codes never enter this package API. */
typealias BrowserSemanticKey = WireBrowserSemanticKey

sealed interface BrowserInput {
    val epoch: Long
    val sequence: Long

    data class Semantic(
        override val epoch: Long,
        override val sequence: Long,
        val key: BrowserSemanticKey,
    ) : BrowserInput

    data class Text(
        override val epoch: Long,
        override val sequence: Long,
        val text: String,
    ) : BrowserInput {
        override fun toString(): String = "BrowserInput.Text(epoch=$epoch, sequence=$sequence, text=<redacted>)"
    }
}

enum class BrowserTextRejection {
    EMPTY,
    TOO_LONG,
    CONTROL_CHARACTER,
    MALFORMED_UNICODE,
}

/** Explicit input bound shared by text dispatch and dialog prompt answers. */
object BrowserTextPolicy {
    const val MAX_UTF8_BYTES: Int = 4 * 1024

    fun validate(value: String): BrowserTextValidation {
        if (value.isEmpty()) return BrowserTextValidation.Rejected(BrowserTextRejection.EMPTY)
        if (hasUnpairedSurrogate(value)) return BrowserTextValidation.Rejected(BrowserTextRejection.MALFORMED_UNICODE)
        if (value.any(::isControlCharacter)) return BrowserTextValidation.Rejected(BrowserTextRejection.CONTROL_CHARACTER)
        val bytes = value.toByteArray(StandardCharsets.UTF_8).size
        if (bytes > MAX_UTF8_BYTES) return BrowserTextValidation.Rejected(BrowserTextRejection.TOO_LONG)
        return BrowserTextValidation.Accepted(BrowserText(value, bytes))
    }

    private fun hasUnpairedSurrogate(value: String): Boolean {
        var index = 0
        while (index < value.length) {
            when {
                Character.isHighSurrogate(value[index]) -> {
                    if (index + 1 == value.length || !Character.isLowSurrogate(value[index + 1])) return true
                    index += 2
                }
                Character.isLowSurrogate(value[index]) -> return true
                else -> index += 1
            }
        }
        return false
    }

    private fun isControlCharacter(character: Char): Boolean = Character.isISOControl(character)
}

sealed interface BrowserTextValidation {
    data class Accepted(val text: BrowserText) : BrowserTextValidation
    data class Rejected(val reason: BrowserTextRejection) : BrowserTextValidation {
        override fun toString(): String = "BrowserTextValidation.Rejected(reason=$reason, text=<redacted>)"
    }
}

/** The only text value allowed to reach a native IME dispatch boundary. */
class BrowserText internal constructor(
    val value: String,
    val utf8Bytes: Int,
) {
    override fun toString(): String = "BrowserText(<redacted utf8Bytes=$utf8Bytes>)"
}

/** Symbolic native actions, intentionally not Android key-code values. */
enum class BrowserNativeKey {
    UP,
    DOWN,
    LEFT,
    RIGHT,
    SELECT,
    BACK,
    TAB,
    ESCAPE,
    PAGE_UP,
    PAGE_DOWN,
    HOME,
    END,
    REFRESH,

    /** Submits a focused text field. Not the same key as SELECT, and not interchangeable. */
    ENTER,
}

sealed interface BrowserNativeInput {
    data class KeyStroke(
        val key: BrowserNativeKey,
        val shift: Boolean = false,
    ) : BrowserNativeInput

    data class ComposedText(val text: BrowserText) : BrowserNativeInput {
        override fun toString(): String = "BrowserNativeInput.ComposedText(text=<redacted utf8Bytes=${text.utf8Bytes}>)"
    }

    data class Pointer(
        val action: com.rextechnologies.flint.protocol.wire.BrowserPointerAction,
        val x: Int,
        val y: Int,
        val buttons: Int,
    ) : BrowserNativeInput

    data class Scroll(
        val x: Int,
        val y: Int,
        val deltaX: Int,
        val deltaY: Int,
    ) : BrowserNativeInput
}

enum class BrowserInputRejection {
    INVALID_IDENTIFIER,
    STALE_EPOCH,
    STALE_SEQUENCE,
    REMOTE_INPUT_DISABLED,
    BROWSER_SURFACE_INACTIVE,
    INVALID_TEXT,
}

sealed interface BrowserInputMapping {
    data class Accepted(val input: BrowserNativeInput) : BrowserInputMapping
    data class Rejected(val reason: BrowserInputRejection) : BrowserInputMapping {
        override fun toString(): String = "BrowserInputMapping.Rejected(reason=$reason, payload=<redacted>)"
    }
}

/** Maps the complete, reviewed portable allow-list to symbolic native actions. */
class BrowserInputMapper {
    fun map(input: BrowserInput): BrowserInputMapping = when (input) {
        is BrowserInput.Semantic -> BrowserInputMapping.Accepted(
            BrowserNativeInput.KeyStroke(
                key = when (input.key) {
                    BrowserSemanticKey.UP -> BrowserNativeKey.UP
                    BrowserSemanticKey.DOWN -> BrowserNativeKey.DOWN
                    BrowserSemanticKey.LEFT -> BrowserNativeKey.LEFT
                    BrowserSemanticKey.RIGHT -> BrowserNativeKey.RIGHT
                    BrowserSemanticKey.SELECT -> BrowserNativeKey.SELECT
                    BrowserSemanticKey.BACK -> BrowserNativeKey.BACK
                    BrowserSemanticKey.TAB -> BrowserNativeKey.TAB
                    BrowserSemanticKey.SHIFT_TAB -> BrowserNativeKey.TAB
                    BrowserSemanticKey.ESCAPE -> BrowserNativeKey.ESCAPE
                    BrowserSemanticKey.PAGE_UP -> BrowserNativeKey.PAGE_UP
                    BrowserSemanticKey.PAGE_DOWN -> BrowserNativeKey.PAGE_DOWN
                    BrowserSemanticKey.HOME -> BrowserNativeKey.HOME
                    BrowserSemanticKey.END -> BrowserNativeKey.END
                    BrowserSemanticKey.REFRESH -> BrowserNativeKey.REFRESH
                },
                shift = input.key == BrowserSemanticKey.SHIFT_TAB,
            ),
        )
        is BrowserInput.Text -> when (val validation = BrowserTextPolicy.validate(input.text)) {
            is BrowserTextValidation.Accepted -> BrowserInputMapping.Accepted(BrowserNativeInput.ComposedText(validation.text))
            is BrowserTextValidation.Rejected -> BrowserInputMapping.Rejected(BrowserInputRejection.INVALID_TEXT)
        }
    }
}

data class BrowserInputState(
    val epoch: Long = 0,
    val lastSequence: Long = 0,
    val remoteInputEnabled: Boolean = false,
    val surface: BrowserSurfaceOwner = BrowserSurfaceOwner.IDLE,
)

data class BrowserInputTransition(
    val state: BrowserInputState,
    val mapping: BrowserInputMapping,
)

/** Rejects stale input before mapping or dispatching it to a WebView. */
class BrowserInputReducer(
    private val mapper: BrowserInputMapper = BrowserInputMapper(),
) {
    fun reduce(state: BrowserInputState, input: BrowserInput): BrowserInputTransition {
        val rejection = validationRejection(state, input)
        if (rejection != null) {
            return BrowserInputTransition(state, BrowserInputMapping.Rejected(rejection))
        }
        val mapping = mapper.map(input)
        return if (mapping is BrowserInputMapping.Accepted) {
            BrowserInputTransition(state.copy(lastSequence = input.sequence), mapping)
        } else {
            BrowserInputTransition(state, mapping)
        }
    }

    private fun validationRejection(
        state: BrowserInputState,
        input: BrowserInput,
    ): BrowserInputRejection? = when {
        input.epoch <= 0 || input.sequence <= 0 -> BrowserInputRejection.INVALID_IDENTIFIER
        state.surface != BrowserSurfaceOwner.BROWSER -> BrowserInputRejection.BROWSER_SURFACE_INACTIVE
        !state.remoteInputEnabled -> BrowserInputRejection.REMOTE_INPUT_DISABLED
        input.epoch != state.epoch -> BrowserInputRejection.STALE_EPOCH
        state.lastSequence == Long.MAX_VALUE || input.sequence != state.lastSequence + 1 ->
            BrowserInputRejection.STALE_SEQUENCE
        else -> null
    }
}
