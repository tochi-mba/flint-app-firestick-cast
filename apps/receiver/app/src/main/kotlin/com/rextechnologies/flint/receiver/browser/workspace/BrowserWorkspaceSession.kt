package com.rextechnologies.flint.receiver.browser.workspace

import android.os.Handler
import android.os.Looper
import android.util.Log
import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.protocol.wire.BrowserSemanticKey
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import com.rextechnologies.flint.receiver.browser.BrowserNativeKey
import com.rextechnologies.flint.receiver.browser.BrowserPaneAudioMuteResult
import com.rextechnologies.flint.receiver.browser.BrowserStateEvent
import com.rextechnologies.flint.receiver.browser.BrowserTextPolicy
import com.rextechnologies.flint.receiver.browser.BrowserTextValidation
import com.rextechnologies.flint.receiver.browser.BrowserUrlPolicy
import com.rextechnologies.flint.receiver.browser.BrowserUrlResult
import com.rextechnologies.flint.receiver.browser.BrowserWebViewDriver
import com.rextechnologies.flint.receiver.browser.workspace.BrowserWorkspaceCapacity
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Service-owned workspace session: pure controller + ordered effect application onto the Activity
 * host. Compose must never own pane collections with `remember`.
 */
class BrowserWorkspaceSession(
    private val controller: BrowserWorkspaceController = BrowserWorkspaceController(),
    private val mainHandler: Handler = Handler(Looper.getMainLooper()),
    private val notice: (String) -> Unit = {},
    private val workspaceStore: com.rextechnologies.flint.receiver.browser.BrowserWorkspaceStore? = null,
    private val onStateChanged: () -> Unit = {},
) {
    @Volatile
    private var host: BrowserWorkspaceHostPort? = null
    private val pointerRouter = BrowserWorkspacePointerRouter { paneId, input ->
        host?.dispatchNativeInput(paneId, input)
    }
    private val observableState = MutableStateFlow(controller.state)
    val stateFlow = observableState.asStateFlow()
    private var nextRendererToken = 0L
    private val rendererTokens = LinkedHashMap<Long, Long>()
    private val dialogs = BrowserWorkspaceDialogs()
    internal val pendingDialog = dialogs.pending

    /** Opaque generations for drivers created under this session. */
    private val generations = LinkedHashMap<Long, Long>()

    /** Accumulated page chrome per pane until the reducer accepts a [PageStateReported]. */
    private val pages = LinkedHashMap<Long, BrowserWorkspacePage>()

    val state: BrowserWorkspaceState get() = controller.state

    val capacity: BrowserWorkspaceCapacity get() = controller.capacity

    /** Focused pane's live driver when the Activity host has allocated a WebView for it. */
    fun focusedDriver(): BrowserWebViewDriver? =
        host?.driverFor(state.focusedPaneId)

    /** Mosaic root (every live pane) for the Windows JPEG preview; null when the host is gone. */
    fun previewCaptureView(): android.view.View? = host?.previewCaptureView()

    internal fun attachHost(next: BrowserWorkspaceHostPort?) {
        if (host === next) return
        pointerRouter.cancel()
        dialogs.cancel()
        host?.destroyAll()
        rendererTokens.clear()
        host = next
        if (next != null) {
            dispatch(BrowserWorkspaceAction.RebuildRenderers)
        }
    }

    internal fun detachHost(expected: BrowserWorkspaceHostPort) {
        if (host === expected) attachHost(null)
    }

    fun isOpen(): Boolean = state.profile != null && state.panes.isNotEmpty()

    fun openLocalWorkspace(profileId: String, seedUrl: String?) {
        val snapshot = workspaceStore?.get(profileId)
        dispatch(BrowserWorkspaceAction.ActivateProfile(BrowserWorkspaceProfile.LocalTv(profileId), snapshot))
        if (state.panes.isEmpty()) {
            dispatch(BrowserWorkspaceAction.OpenPane(seedUrl?.takeIf { it.isNotBlank() }))
        }
    }

    fun saveLocalState() {
        state.snapshotForPersistence()?.let { workspaceStore?.put(it.ownerProfileId, it) }
    }

    fun closeWorkspace() {
        dispatch(BrowserWorkspaceAction.DeactivateProfile)
        generations.clear()
        pages.clear()
        host?.destroyAll()
    }

    /**
     * One rung of the workspace Back ladder.
     *
     * @return true when Back was consumed inside the workspace; false when the caller should leave.
     */
    fun handleBack(): Boolean {
        val snapshot = state
        val focused = snapshot.focusedPane
        if (snapshot.pageFullscreenPaneId != null && focused != null) {
            dispatch(
                BrowserWorkspaceAction.SetPageFullscreen(
                    paneId = focused.id,
                    rendererGeneration = focused.rendererGeneration,
                    active = false,
                ),
            )
            return true
        }
        if (snapshot.theaterPaneId != null) {
            dispatch(BrowserWorkspaceAction.ExitTheaterMode)
            return true
        }
        if (snapshot.interactionMode == BrowserWorkspaceInteractionMode.PAGE) {
            dispatch(
                BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.WORKSPACE_CHROME),
            )
            return true
        }
        return false
    }

    fun dispatch(action: BrowserWorkspaceAction): BrowserWorkspaceTransition {
        val before = state
        val transition = controller.dispatch(action)
        val beforePane = before.focusedPane
        val afterPane = transition.state.focusedPane
        if (before.profile != transition.state.profile || before.focusedPaneId != transition.state.focusedPaneId ||
            before.interactionMode != transition.state.interactionMode ||
            before.layout != transition.state.layout || before.split != transition.state.split ||
            before.paneIdsInSlotOrder != transition.state.paneIdsInSlotOrder ||
            before.theaterPaneId != transition.state.theaterPaneId ||
            beforePane?.rendererGeneration != afterPane?.rendererGeneration ||
            beforePane?.rendererResidency != afterPane?.rendererResidency ||
            beforePane?.page?.navigationId != afterPane?.page?.navigationId
        ) {
            // The old native host still exists here, before freeze/destroy effects are applied.
            pointerRouter.cancel()
        }
        observableState.value = transition.state
        pendingDialog.value?.let { request ->
            val pane = transition.state.pane(request.paneId)
            if (before.profile != transition.state.profile || pane == null ||
                pane.rendererGeneration != before.pane(request.paneId)?.rendererGeneration ||
                pane.page.navigationId != before.pane(request.paneId)?.page?.navigationId ||
                transition.state.focusedPaneId != request.paneId
            ) {
                dialogs.cancel()
            }
        }
        applyEffects(transition.effects)
        renderHost()
        onStateChanged()
        return transition
    }

    fun dispatchRemoteText(paneId: Long, text: String) {
        if (state.focusedPaneId != paneId ||
            state.interactionMode != BrowserWorkspaceInteractionMode.PAGE
        ) {
            return
        }
        val validated = when (val result = BrowserTextPolicy.validate(text)) {
            is BrowserTextValidation.Accepted -> result.text
            is BrowserTextValidation.Rejected -> return
        }
        runOnUi {
            host?.driverFor(paneId)?.dispatch(BrowserNativeInput.ComposedText(validated))
        }
    }

    fun dispatchRemoteKey(paneId: Long, key: BrowserSemanticKey) {
        if (state.focusedPaneId != paneId ||
            state.interactionMode != BrowserWorkspaceInteractionMode.PAGE
        ) {
            return
        }
        val nativeKey = key.toNativeKey() ?: return
        runOnUi {
            host?.driverFor(paneId)?.dispatch(
                BrowserNativeInput.KeyStroke(
                    nativeKey,
                    shift = key == BrowserSemanticKey.SHIFT_TAB,
                ),
            )
        }
    }

    fun onPaneStateEvent(paneId: Long, token: Long, event: BrowserStateEvent) {
        if (rendererTokens[paneId] != token) return
        val generation = generations[paneId] ?: return
        val current = pages[paneId] ?: state.pane(paneId)?.page ?: BrowserWorkspacePage()
        val next = when (event) {
            is BrowserStateEvent.OpenAccepted -> current.copy(
                url = event.address.canonicalUrl,
                loading = true,
                progressPercent = 0,
                navigationId = event.commandId,
            )
            is BrowserStateEvent.BlankOpened -> current.copy(
                url = "",
                title = "",
                loading = false,
                progressPercent = 100,
                navigationId = event.commandId,
            )
            is BrowserStateEvent.NavigationAccepted -> current.copy(
                url = event.address.canonicalUrl,
                loading = true,
                progressPercent = 0,
                navigationId = event.commandId,
            )
            is BrowserStateEvent.Progress -> current.copy(
                navigationId = event.navigationId,
                loading = event.percent < 100,
                progressPercent = event.percent,
            )
            is BrowserStateEvent.Title -> current.copy(
                navigationId = event.navigationId,
                title = event.title,
            )
            is BrowserStateEvent.PageFinished -> current.copy(
                navigationId = event.navigationId,
                loading = false,
                progressPercent = 100,
                canGoBack = event.canGoBack,
                canGoForward = event.canGoForward,
            )
            is BrowserStateEvent.Failed -> current.copy(
                navigationId = event.navigationId,
                loading = false,
            )
            else -> return
        }
        if (next.navigationId < maxOf(current.navigationId, state.pane(paneId)?.page?.navigationId ?: 0)) return
        pages[paneId] = next
        dispatch(
            BrowserWorkspaceAction.PageStateReported(
                paneId = paneId,
                rendererGeneration = generation,
                page = next,
            ),
        )
    }

    fun onRendererGone(paneId: Long, token: Long) {
        if (rendererTokens[paneId] != token) return
        val generation = generations[paneId] ?: return
        dispatch(
            BrowserWorkspaceAction.RendererFailed(
                paneId = paneId,
                rendererGeneration = generation,
                reason = BrowserWorkspaceRendererFailure.RENDERER_PROCESS_GONE,
            ),
        )
    }

    fun viewportFor(paneId: Long): Pair<Int, Int>? = host?.driverFor(paneId)?.viewport()

    /**
     * Windows mosaic preview pointer: hit-test the pane under the cursor, select it on DOWN, and
     * deliver the gesture to that page even when it was not previously focused.
     */
    fun dispatchPreviewPointer(
        mosaicX: Int,
        mosaicY: Int,
        pointer: BrowserNativeInput.Pointer,
    ): Boolean {
        val hit = host?.hitTestMosaic(mosaicX, mosaicY) ?: return false
        val (paneId, local) = hit
        if (pointer.action == BrowserPointerAction.DOWN) {
            selectPaneForPreviewInteraction(paneId)
        }
        pointerRouter.dispatch(
            paneId,
            BrowserNativeInput.Pointer(pointer.action, local.first, local.second, pointer.buttons),
        )
        return true
    }

    /**
     * Windows mosaic preview scroll: route the wheel to the pane under the cursor and select it.
     */
    fun dispatchPreviewScroll(
        mosaicX: Int,
        mosaicY: Int,
        scroll: BrowserNativeInput.Scroll,
    ): Boolean {
        val hit = host?.hitTestMosaic(mosaicX, mosaicY) ?: return false
        val (paneId, local) = hit
        selectPaneForPreviewInteraction(paneId)
        pointerRouter.dispatch(
            paneId,
            BrowserNativeInput.Scroll(local.first, local.second, scroll.deltaX, scroll.deltaY),
        )
        return true
    }

    private fun selectPaneForPreviewInteraction(paneId: Long) {
        if (state.focusedPaneId != paneId) {
            dispatch(BrowserWorkspaceAction.FocusPane(paneId))
        }
        if (state.interactionMode != BrowserWorkspaceInteractionMode.PAGE) {
            dispatch(BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.PAGE))
        }
    }

    fun dispatchNativeInput(paneId: Long, input: BrowserNativeInput) {
        if (state.focusedPaneId != paneId || state.interactionMode != BrowserWorkspaceInteractionMode.PAGE) return
        pointerRouter.dispatch(paneId, input)
    }

    fun onPageFullscreen(paneId: Long, token: Long, active: Boolean) {
        if (rendererTokens[paneId] != token) return
        val generation = generations[paneId] ?: return
        dispatch(BrowserWorkspaceAction.SetPageFullscreen(paneId, generation, active))
    }

    fun onDialog(paneId: Long, token: Long, dialog: com.rextechnologies.flint.receiver.browser.PendingJsDialog) {
        if (rendererTokens[paneId] != token || state.focusedPaneId != paneId) {
            dialog.resolve(com.rextechnologies.flint.receiver.browser.BrowserDialogAnswer.Cancel)
            return
        }
        dispatch(BrowserWorkspaceAction.SetInteractionMode(BrowserWorkspaceInteractionMode.WORKSPACE_CHROME))
        val id = dialogs.show(paneId, token, dialog)
        mainHandler.postDelayed({
            dialogs.answer(id, com.rextechnologies.flint.receiver.browser.BrowserDialogAnswer.Cancel)
        }, 60_000)
    }

    internal fun answerDialog(id: Long, answer: com.rextechnologies.flint.receiver.browser.BrowserDialogAnswer) =
        dialogs.answer(id, answer)

    private fun applyEffects(effects: List<BrowserWorkspaceEffect>) {
        for (effect in effects) {
            when (effect) {
                is BrowserWorkspaceEffect.PersistLocalWorkspace -> {
                    workspaceStore?.put(effect.profileId, effect.snapshot)
                }
                is BrowserWorkspaceEffect.DiscardEphemeralWorkspace -> Unit
                is BrowserWorkspaceEffect.RendererFailureObserved -> Unit
                is BrowserWorkspaceEffect.Refused -> notice(refusalMessage(effect.reason))
                is BrowserWorkspaceEffect.FocusChanged,
                is BrowserWorkspaceEffect.LayoutChanged,
                is BrowserWorkspaceEffect.InteractionModeChanged,
                is BrowserWorkspaceEffect.TheaterModeChanged,
                is BrowserWorkspaceEffect.ProfileChanged,
                -> Unit
                is BrowserWorkspaceEffect.PageFullscreenChanged -> {
                    if (!effect.active) host?.exitFullscreen(effect.paneId)
                }
                else -> applyHostEffect(effect)
            }
        }
        val binding = host ?: return
        runOnUi {
            binding.setPageFocusEnabled(state.interactionMode == BrowserWorkspaceInteractionMode.PAGE)
        }
    }

    private fun applyHostEffect(effect: BrowserWorkspaceEffect) {
        val binding = host ?: return
        when (effect) {
            is BrowserWorkspaceEffect.CreateRenderer -> {
                generations[effect.paneId] = effect.rendererGeneration
                pages[effect.paneId] = BrowserWorkspacePage(url = effect.initialUrl.orEmpty())
                runOnUi {
                    val token = ++nextRendererToken
                    rendererTokens[effect.paneId] = token
                    binding.create(effect.paneId, token, effect.initialUrl)
                    effect.initialUrl?.takeIf { it.isNotBlank() }?.let { url ->
                        navigateDriver(effect.paneId, effect.rendererGeneration, url)
                    }
                }
            }
            is BrowserWorkspaceEffect.RestoreRenderer -> {
                generations[effect.paneId] = effect.rendererGeneration
                runOnUi {
                    val token = ++nextRendererToken
                    rendererTokens[effect.paneId] = token
                    if (!binding.restore(effect.paneId, token) && effect.url.isNotBlank()) {
                        navigateDriver(
                            effect.paneId,
                            effect.rendererGeneration,
                            effect.url,
                            state.pane(effect.paneId)?.page?.navigationId ?: 1,
                        )
                    }
                }
            }
            is BrowserWorkspaceEffect.FreezeRenderer -> {
                rendererTokens.remove(effect.paneId)
                runOnUi { binding.freeze(effect.paneId) }
            }
            is BrowserWorkspaceEffect.DestroyPane -> {
                generations.remove(effect.paneId)
                rendererTokens.remove(effect.paneId)
                pages.remove(effect.paneId)
                runOnUi { binding.destroy(effect.paneId) }
            }
            is BrowserWorkspaceEffect.NavigateRenderer -> {
                navigateDriver(effect.paneId, effect.rendererGeneration, effect.url, effect.navigationId)
            }
            is BrowserWorkspaceEffect.ReloadRenderer -> {
                runOnUi {
                    resumeDriver(effect.paneId, effect.navigationId)
                    binding.driverFor(effect.paneId)?.reload()
                }
            }
            is BrowserWorkspaceEffect.GoBackRenderer -> {
                runOnUi {
                    resumeDriver(effect.paneId, effect.navigationId)
                    binding.driverFor(effect.paneId)?.goBack()
                }
            }
            is BrowserWorkspaceEffect.GoForwardRenderer -> {
                runOnUi {
                    resumeDriver(effect.paneId, effect.navigationId)
                    binding.driverFor(effect.paneId)?.goForward()
                }
            }
            is BrowserWorkspaceEffect.RequestMediaPlayPause -> {
                runOnUi {
                    val result = binding.driverFor(effect.paneId)?.let { driver ->
                        driver.dispatchPlayPause()
                        BrowserWorkspaceMediaDispatchResult.DISPATCHED
                    } ?: BrowserWorkspaceMediaDispatchResult.REJECTED
                    dispatch(
                        BrowserWorkspaceAction.PlaybackRequestCompleted(
                            paneId = effect.paneId,
                            rendererGeneration = effect.rendererGeneration,
                            requestId = effect.requestId,
                            result = result,
                        ),
                    )
                }
            }
            is BrowserWorkspaceEffect.ApplyPaneMute -> {
                runOnUi {
                    val driver = binding.driverFor(effect.paneId)
                    if (driver == null) {
                        dispatch(
                            BrowserWorkspaceAction.MuteApplicationCompleted(
                                paneId = effect.paneId,
                                rendererGeneration = effect.rendererGeneration,
                                requestId = effect.requestId,
                                result = BrowserWorkspaceMuteResult.FAILED,
                            ),
                        )
                        return@runOnUi
                    }
                    driver.setAudioMuted(effect.muted) { result ->
                        val mapped = when (result) {
                            BrowserPaneAudioMuteResult.APPLIED -> BrowserWorkspaceMuteResult.APPLIED_TO_RENDERER
                            BrowserPaneAudioMuteResult.UNSUPPORTED -> BrowserWorkspaceMuteResult.UNSUPPORTED
                            BrowserPaneAudioMuteResult.FAILED -> BrowserWorkspaceMuteResult.FAILED
                        }
                        dispatch(
                            BrowserWorkspaceAction.MuteApplicationCompleted(
                                paneId = effect.paneId,
                                rendererGeneration = effect.rendererGeneration,
                                requestId = effect.requestId,
                                result = mapped,
                            ),
                        )
                    }
                }
            }
            else -> Unit
        }
    }

    private fun navigateDriver(
        paneId: Long,
        generation: Long,
        url: String,
        navigationId: Long = 1,
    ) {
        if (generations[paneId] != generation) return
        val address = when (val result = BrowserUrlPolicy().evaluate(url)) {
            is BrowserUrlResult.Accepted -> result.url
            is BrowserUrlResult.Rejected -> {
                Log.w(TAG, "workspace navigate rejected for pane $paneId")
                return
            }
        }
        runOnUi {
            host?.driverFor(paneId)?.navigate(epoch = 1, commandId = navigationId, address = address)
        }
    }

    private fun resumeDriver(paneId: Long, navigationId: Long) {
        host?.driverFor(
            paneId,
        )?.resume(com.rextechnologies.flint.receiver.browser.BrowserState(epoch = 1, navigationId = navigationId))
        pages[paneId] = state.pane(paneId)?.page ?: return
    }

    private fun renderHost() {
        val binding = host ?: return
        val snapshot = state
        runOnUi {
            binding.render(snapshot)
        }
    }

    private fun runOnUi(block: () -> Unit) {
        if (Looper.myLooper() == Looper.getMainLooper()) {
            block()
        } else {
            mainHandler.post(block)
        }
    }

    private fun refusalMessage(reason: BrowserWorkspaceRefusal): String = when (reason) {
        BrowserWorkspaceRefusal.INVALID_URL -> "Enter a valid secure https address."
        BrowserWorkspaceRefusal.PANE_CAPACITY_REACHED,
        BrowserWorkspaceRefusal.RENDERER_CAPACITY_BLOCKED,
        -> "This TV already has as many live pages as it can keep open."
        BrowserWorkspaceRefusal.LAYOUT_NOT_AVAILABLE ->
            "That layout is not available on this television."
        BrowserWorkspaceRefusal.LAYOUT_DOES_NOT_FIT_PANES ->
            "That layout needs a different number of open pages. Add or close a pane first."
        BrowserWorkspaceRefusal.UNKNOWN_PANE -> "That page is no longer open."
        BrowserWorkspaceRefusal.NO_ACTIVE_PROFILE -> "Choose a TV profile before opening a workspace."
        BrowserWorkspaceRefusal.DEVICE_PROFILE_CANNOT_RESTORE_SNAPSHOT ->
            "A Windows-device profile cannot restore a saved TV workspace."
        BrowserWorkspaceRefusal.EXCLUSIVE_PRESENTATION_ACTIVE ->
            "Finish fullscreen or theater mode first."
        BrowserWorkspaceRefusal.RENDERER_NOT_RESIDENT -> "That page is suspended — select it to restore."
        BrowserWorkspaceRefusal.INVALID_SLOT -> "That page slot is not available."
        BrowserWorkspaceRefusal.PANE_ID_EXHAUSTED,
        BrowserWorkspaceRefusal.MEDIA_REQUEST_ID_EXHAUSTED,
        -> "The workspace ran out of internal identifiers."
        BrowserWorkspaceRefusal.INVALID_SNAPSHOT,
        BrowserWorkspaceRefusal.SNAPSHOT_EXCEEDS_CAPACITY,
        -> "The saved workspace could not be restored."
    }

    companion object {
        private const val TAG = "FlintWorkspace"
    }
}

private fun BrowserSemanticKey.toNativeKey(): BrowserNativeKey? = when (this) {
    BrowserSemanticKey.UP -> BrowserNativeKey.UP
    BrowserSemanticKey.DOWN -> BrowserNativeKey.DOWN
    BrowserSemanticKey.LEFT -> BrowserNativeKey.LEFT
    BrowserSemanticKey.RIGHT -> BrowserNativeKey.RIGHT
    BrowserSemanticKey.SELECT -> BrowserNativeKey.SELECT
    BrowserSemanticKey.BACK -> BrowserNativeKey.BACK
    BrowserSemanticKey.TAB, BrowserSemanticKey.SHIFT_TAB -> BrowserNativeKey.TAB
    BrowserSemanticKey.ESCAPE -> BrowserNativeKey.ESCAPE
    BrowserSemanticKey.PAGE_UP -> BrowserNativeKey.PAGE_UP
    BrowserSemanticKey.PAGE_DOWN -> BrowserNativeKey.PAGE_DOWN
    BrowserSemanticKey.HOME -> BrowserNativeKey.HOME
    BrowserSemanticKey.END -> BrowserNativeKey.END
    BrowserSemanticKey.REFRESH -> BrowserNativeKey.REFRESH
    else -> null
}
