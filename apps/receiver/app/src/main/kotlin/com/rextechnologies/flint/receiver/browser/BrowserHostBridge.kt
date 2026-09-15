package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserDialogMessage
import com.rextechnologies.flint.protocol.wire.BrowserDialogType
import com.rextechnologies.flint.receiver.BrowserDialogUi
import com.rextechnologies.flint.receiver.browser.net.BrowserOutboundMessage
import com.rextechnologies.flint.receiver.browser.net.BrowserTlsServer

/**
 * Owns page-dialog presentation, clear-data confirmation, and the opt-in preview capture loop.
 *
 * Kept out of [com.rextechnologies.flint.receiver.ReceiverService] so the service stays a router
 * rather than accumulating every browser side-effect.
 */
class BrowserHostBridge(
    private val coordinator: BrowserCoordinator,
    private val publishDialog: (BrowserDialogUi?) -> Unit,
    private val sendOutbound: (BrowserOutboundMessage) -> Boolean,
    private val onClearData: () -> Unit = {},
) {
    private val dialogReducer = BrowserDialogReducer()
    private var dialogState = BrowserDialogState()
    private var pendingJsResolve: ((BrowserDialogAnswer) -> Unit)? = null
    private var nextDialogId: Long = 1
    private var pendingClearData: Boolean = false
    private var attachedDriver: BrowserWebViewDriver? = null
    private var previewLoop: BrowserPreviewLoop? = null
    private var previewPublisher: BrowserPreviewPublisher? = null

    /** Host-requested enablement; reapplied when the capture target WebView changes. */
    private var previewDesired = false

    fun bindPreviewPublisher(publisher: BrowserPreviewPublisher?) {
        previewPublisher = publisher
        if (publisher == null) {
            previewLoop?.detach()
            previewLoop = null
        } else if (previewDesired) {
            publisher.setEnabled(true)
            previewLoop?.setEnabled(true)
        }
    }

    fun attachDriver(driver: BrowserWebViewDriver) {
        if (attachedDriver === driver && previewLoop != null) {
            if (previewDesired) {
                previewPublisher?.setEnabled(true)
                previewLoop?.setEnabled(true)
            }
            return
        }
        attachedDriver = driver
        attachPreviewTarget(driver.webView())
    }

    /**
     * Points JPEG capture at [view] — a single WebView for tabs, or the workspace mosaic root so
     * Windows sees every live pane the sofa sees.
     */
    fun attachPreviewTarget(view: android.view.View) {
        val existing = previewLoop
        if (existing?.attachedTarget() === view) {
            if (previewDesired) {
                previewPublisher?.setEnabled(true)
                existing.setEnabled(true)
            }
            return
        }
        previewLoop?.detach()
        val publisher = previewPublisher ?: return
        val loop = BrowserPreviewLoop(
            capture = BrowserPreviewCapture(publisher),
            epoch = { coordinator.snapshot().epoch ?: 0L },
            navigationId = { coordinator.snapshot().navigationId },
        )
        loop.attach(view)
        previewLoop = loop
        if (previewDesired) {
            publisher.setEnabled(true)
            loop.setEnabled(true)
        }
    }

    fun detachDriver(driver: BrowserWebViewDriver) {
        if (attachedDriver !== driver) return
        previewLoop?.detach()
        previewLoop = null
        attachedDriver = null
        cancelActiveDialog()
    }

    fun setPreviewEnabled(enabled: Boolean) {
        previewDesired = enabled
        previewPublisher?.setEnabled(enabled)
        previewLoop?.setEnabled(enabled)
    }

    fun onPageDialog(pending: PendingJsDialog) {
        val origin = originAddress(pending.originUrl) ?: run {
            pending.resolve(BrowserDialogAnswer.Cancel)
            return
        }
        val epoch = coordinator.snapshot().epoch ?: run {
            pending.resolve(BrowserDialogAnswer.Cancel)
            return
        }
        val dialogId = nextDialogId++
        val request = BrowserDialogRequest(
            dialogId = dialogId,
            epoch = epoch,
            kind = pending.kind,
            origin = origin,
            message = BrowserDialogText.fromPage(pending.message),
            defaultValue = pending.defaultValue?.let(BrowserDialogText::fromPage),
        )
        val transition = dialogReducer.reduce(
            dialogState,
            BrowserDialogEvent.Show(request, nowMs = System.currentTimeMillis()),
        )
        dialogState = transition.state
        when (val effect = transition.effect) {
            is BrowserDialogEffect.ShowLocal -> {
                pendingJsResolve = pending.resolve
                publishDialog(
                    BrowserDialogUi(
                        dialogId = dialogId,
                        origin = origin.displayUrl,
                        message = request.message.value,
                        kind = pending.kind.name,
                        defaultValue = request.defaultValue?.value,
                    ),
                )
                sendOutbound(
                    BrowserOutboundMessage.Dialog(
                        BrowserDialogMessage(
                            epoch = epoch,
                            dialogId = dialogId,
                            type = pending.kind.toWireType(),
                            origin = origin.displayUrl,
                            message = request.message.value,
                            defaultValue = request.defaultValue?.value.orEmpty(),
                            timeoutMilliseconds = BrowserDialogReducer.DEFAULT_TIMEOUT_MS.toInt(),
                        ),
                    ),
                )
            }
            else -> pending.resolve(BrowserDialogAnswer.Cancel)
        }
    }

    fun replyFromHost(epoch: Long, dialogId: Long, answer: BrowserDialogAnswer) {
        applyReply(epoch, dialogId, BrowserDialogReplySource.PINNED_SESSION, answer)
    }

    fun replyFromTv(accepted: Boolean, promptText: String? = null) {
        val active = dialogState.active ?: return
        val answer = when {
            !accepted -> BrowserDialogAnswer.Cancel
            promptText != null -> BrowserDialogAnswer.Prompt(promptText)
            else -> BrowserDialogAnswer.Confirm
        }
        applyReply(active.request.epoch, active.request.dialogId, BrowserDialogReplySource.TV_LOCAL, answer)
    }

    fun requestClearData(epoch: Long) {
        if (coordinator.snapshot().epoch != epoch) return
        pendingClearData = true
        val origin = BrowserAddress(
            canonicalUrl = "https://example.com/",
            displayUrl = "Flint browser",
        )
        val dialogId = nextDialogId++
        val request = BrowserDialogRequest(
            dialogId = dialogId,
            epoch = epoch,
            kind = BrowserDialogKind.CONFIRM,
            origin = origin,
            message = BrowserDialogText.fromPage(
                "Clear cookies, cache, and form data for this TV browser? Pairing and security pins stay.",
            ),
        )
        val transition = dialogReducer.reduce(
            dialogState,
            BrowserDialogEvent.Show(request, nowMs = System.currentTimeMillis()),
        )
        dialogState = transition.state
        if (transition.effect !is BrowserDialogEffect.ShowLocal) {
            pendingClearData = false
            return
        }
        pendingJsResolve = { answer ->
            if (answer is BrowserDialogAnswer.Confirm && pendingClearData) {
                attachedDriver?.webView()?.let(BrowserDataClearer::clear)
                onClearData()
            }
            pendingClearData = false
        }
        publishDialog(
            BrowserDialogUi(
                dialogId = dialogId,
                origin = origin.displayUrl,
                message = request.message.value,
                kind = BrowserDialogKind.CONFIRM.name,
            ),
        )
    }

    fun onSessionEnded() {
        cancelActiveDialog()
        // Preview preference is owned by the controller; TLS end calls setPreviewEnabled(false).
        pendingClearData = false
    }

    private fun applyReply(
        epoch: Long,
        dialogId: Long,
        source: BrowserDialogReplySource,
        answer: BrowserDialogAnswer,
    ) {
        val transition = dialogReducer.reduce(
            dialogState,
            BrowserDialogEvent.Reply(epoch, dialogId, source, answer),
        )
        dialogState = transition.state
        when (val effect = transition.effect) {
            is BrowserDialogEffect.ResolvePageDialog -> {
                val resolved = when {
                    !effect.accepted -> BrowserDialogAnswer.Cancel
                    effect.text != null -> BrowserDialogAnswer.Prompt(effect.text.value)
                    else -> BrowserDialogAnswer.Confirm
                }
                pendingJsResolve?.invoke(resolved)
                pendingJsResolve = null
                publishDialog(null)
            }
            else -> Unit
        }
    }

    private fun cancelActiveDialog() {
        val transition = dialogReducer.reduce(dialogState, BrowserDialogEvent.Close)
        dialogState = transition.state
        pendingJsResolve?.invoke(BrowserDialogAnswer.Cancel)
        pendingJsResolve = null
        publishDialog(null)
    }

    private fun originAddress(raw: String): BrowserAddress? {
        val candidate = raw.ifBlank { "https://example.com/" }
        return when (val evaluated = BrowserUrlPolicy().evaluate(candidate)) {
            is BrowserUrlResult.Accepted -> evaluated.url
            is BrowserUrlResult.Rejected -> when (val fallback = BrowserUrlPolicy().evaluate("https://example.com/")) {
                is BrowserUrlResult.Accepted -> fallback.url
                is BrowserUrlResult.Rejected -> null
            }
        }
    }

    private fun BrowserDialogKind.toWireType(): BrowserDialogType = when (this) {
        BrowserDialogKind.ALERT -> BrowserDialogType.ALERT
        BrowserDialogKind.CONFIRM -> BrowserDialogType.CONFIRM
        BrowserDialogKind.PROMPT -> BrowserDialogType.PROMPT
        BrowserDialogKind.BEFORE_UNLOAD -> BrowserDialogType.BEFORE_UNLOAD
    }
}
