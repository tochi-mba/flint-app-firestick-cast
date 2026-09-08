package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserPointerInput
import com.rextechnologies.flint.protocol.wire.BrowserScrollInput
import com.rextechnologies.flint.protocol.wire.BrowserSemanticKeyInput
import com.rextechnologies.flint.protocol.wire.BrowserTextInput

/**
 * Maps authenticated TLS browser input messages into the pure input reducer and optional native
 * dispatch. Pointer/scroll require a current preview frame reference.
 */
class BrowserInputRouter(
    private val reducer: BrowserInputReducer = BrowserInputReducer(),
    private val previewMapper: PreviewInputMapper = PreviewInputMapper(),
    private val onAccepted: (BrowserNativeInput) -> Unit = {},
    private val onPreviewMapped: (PreviewMappedInput) -> Unit = {},
) {
    private var state = BrowserInputState()

    fun enableForBrowserEpoch(epoch: Long) {
        state = state.copy(
            epoch = epoch,
            lastSequence = 0,
            remoteInputEnabled = true,
            surface = BrowserSurfaceOwner.BROWSER,
        )
    }

    fun disable() {
        state = BrowserInputState()
    }

    fun handle(
        message: BrowserInputMessage,
        currentNavigationId: Long = 0,
        currentFrameId: Long = 0,
        viewportWidth: Int = 0,
        viewportHeight: Int = 0,
    ): BrowserInputMapping {
        when (val event = message.event) {
            is BrowserSemanticKeyInput -> {
                val input = BrowserInput.Semantic(message.epoch, message.sequence, event.key)
                return reduceAccepted(input)
            }
            is BrowserTextInput -> {
                val input = BrowserInput.Text(message.epoch, message.sequence, event.text)
                return reduceAccepted(input)
            }
            is BrowserPointerInput -> {
                if (!state.remoteInputEnabled || state.surface != BrowserSurfaceOwner.BROWSER) {
                    return BrowserInputMapping.Rejected(BrowserInputRejection.REMOTE_INPUT_DISABLED)
                }
                if (message.epoch != state.epoch || message.sequence <= state.lastSequence) {
                    return BrowserInputMapping.Rejected(
                        if (message.epoch != state.epoch) {
                            BrowserInputRejection.STALE_EPOCH
                        } else {
                            BrowserInputRejection.STALE_SEQUENCE
                        },
                    )
                }
                state = state.copy(lastSequence = message.sequence)
                val mapped = previewMapper.mapPointer(
                    event,
                    currentNavigationId,
                    currentFrameId,
                    viewportWidth,
                    viewportHeight,
                )
                onPreviewMapped(mapped)
                return when (mapped) {
                    is PreviewMappedInput.Pointer ->
                        BrowserInputMapping.Accepted(
                            BrowserNativeInput.Pointer(mapped.action, mapped.x, mapped.y, mapped.buttons),
                        ).also { accepted -> onAccepted(accepted.input) }
                    is PreviewMappedInput.Rejected ->
                        BrowserInputMapping.Rejected(BrowserInputRejection.INVALID_IDENTIFIER)
                    else -> BrowserInputMapping.Rejected(BrowserInputRejection.INVALID_IDENTIFIER)
                }
            }
            is BrowserScrollInput -> {
                if (!state.remoteInputEnabled || state.surface != BrowserSurfaceOwner.BROWSER) {
                    return BrowserInputMapping.Rejected(BrowserInputRejection.REMOTE_INPUT_DISABLED)
                }
                if (message.epoch != state.epoch || message.sequence <= state.lastSequence) {
                    return BrowserInputMapping.Rejected(
                        if (message.epoch != state.epoch) {
                            BrowserInputRejection.STALE_EPOCH
                        } else {
                            BrowserInputRejection.STALE_SEQUENCE
                        },
                    )
                }
                state = state.copy(lastSequence = message.sequence)
                val mapped = previewMapper.mapScroll(
                    event,
                    currentNavigationId,
                    currentFrameId,
                    viewportWidth,
                    viewportHeight,
                )
                onPreviewMapped(mapped)
                return when (mapped) {
                    is PreviewMappedInput.Scroll ->
                        BrowserInputMapping.Accepted(
                            BrowserNativeInput.Scroll(mapped.x, mapped.y, mapped.deltaX, mapped.deltaY),
                        ).also { accepted -> onAccepted(accepted.input) }
                    is PreviewMappedInput.Rejected ->
                        BrowserInputMapping.Rejected(BrowserInputRejection.INVALID_IDENTIFIER)
                    else -> BrowserInputMapping.Rejected(BrowserInputRejection.INVALID_IDENTIFIER)
                }
            }
            else -> return BrowserInputMapping.Rejected(BrowserInputRejection.INVALID_IDENTIFIER)
        }
    }

    fun snapshot(): BrowserInputState = state

    private fun reduceAccepted(input: BrowserInput): BrowserInputMapping {
        val transition = reducer.reduce(state, input)
        state = transition.state
        if (transition.mapping is BrowserInputMapping.Accepted) {
            onAccepted(transition.mapping.input)
        }
        return transition.mapping
    }
}
