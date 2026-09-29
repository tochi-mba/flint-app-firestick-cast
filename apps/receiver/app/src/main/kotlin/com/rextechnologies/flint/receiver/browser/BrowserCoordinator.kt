package com.rextechnologies.flint.receiver.browser

/**
 * UI-thread WebView operations. The Service never retains an implementation; Activity owns the
 * concrete driver and clears it on detach.
 */
interface BrowserPort {
    fun open(epoch: Long, commandId: Long, address: BrowserAddress)
    fun navigate(epoch: Long, commandId: Long, address: BrowserAddress)
    fun close(epoch: Long, commandId: Long)
    fun goBack()
    fun goForward()
    fun reload()
    fun stop()

    /**
     * Delivers one accepted input to the page.
     *
     * Separate from the navigation commands above because the two arrive on different cadences: a
     * navigation is occasional and a keystroke is not, and the input path must not be made to wait
     * behind anything.
     */
    fun dispatch(input: BrowserNativeInput)

    /**
     * The page's viewport in pixels, or `null` before the view has been laid out.
     *
     * Pointer coordinates arrive normalised to the preview the host is showing, so they can only be
     * placed on the page once its real size is known. Reporting a guess here puts every click
     * somewhere near, but not on, the thing the user aimed at.
     */
    fun viewport(): Pair<Int, Int>?
}

/**
 * Owns browser epoch/session policy and publishes safe state. It never retains a WebView.
 */
class BrowserCoordinator(
    private val urlPolicy: BrowserUrlPolicy = BrowserUrlPolicy(),
    private val commandReducer: BrowserCommandReducer = BrowserCommandReducer(),
    private val stateReducer: BrowserStateReducer = BrowserStateReducer(),
    private val publish: (BrowserState) -> Unit = {},
) {
    private var commandState = BrowserCommandState()
    private var pageState = BrowserState()
    private var port: BrowserPort? = null

    /**
     * The navigation that arrived before there was anything to show it on.
     *
     * The host's OPEN reaches this coordinator while the television is still on its idle screen: the
     * command is what *causes* the browser surface to appear, and the Activity only creates the
     * WebView once it has. So the port is reliably absent at the moment the first URL arrives, and
     * dropping it there leaves a blank page that every layer reports as a success.
     */
    private var pendingNavigation: Triple<Long, Long, BrowserAddress>? = null

    /**
     * Attaches the foreground page and reports whether a queued navigation was replayed.
     *
     * The return value matters when restoring a tab: a restored driver should reload its saved
     * address only when OPEN did not just replay the same address into it.
     */
    fun attachPort(port: BrowserPort): Boolean {
        this.port = port
        // Replay whatever arrived while there was no page to send it to. This is the ordinary path
        // for the first navigation of a session, not an edge case.
        val pending = pendingNavigation
        pending?.let { (epoch, commandId, address) ->
            pendingNavigation = null
            port.open(epoch, commandId, address)
        }
        return pending != null
    }

    /**
     * Drops a queued OPEN/NAVIGATE without applying it.
     *
     * Needed when activating a tab that already has its own address (host New with URL). Replaying
     * the pending OPEN into that WebView loads the wrong page, and [BrowserWebViewDriver.resume]
     * used to refuse to overwrite a non-blank view — two different Google searches both became
     * the OPEN query (2026-09-08).
     */
    fun discardPendingNavigation() {
        pendingNavigation = null
    }

    fun detachPort(port: BrowserPort) {
        if (this.port === port) {
            this.port = null
        }
    }

    /**
     * Forwards an accepted input to the attached page, if there is one.
     *
     * Dropped silently when no port is attached. That is the ordinary state between a session
     * opening and the Activity creating its view, and an input arriving in that window is stale by
     * the time a view exists rather than something to report.
     */
    fun dispatchInput(input: BrowserNativeInput) {
        port?.dispatch(input)
    }

    /** The attached page's viewport, or `null` when nothing is attached or laid out yet. */
    fun viewport(): Pair<Int, Int>? = port?.viewport()

    /**
     * History and load controls, forwarded to the attached page.
     *
     * These exist on [BrowserPort] and on the wire as BACK/FORWARD/RELOAD/STOP, but nothing routed
     * them: the service logged those four actions and dropped them, so every history button on the
     * host was inert while the transport reported success. Dropped silently with nothing attached,
     * for the same reason input is — there is no page for the request to mean anything to yet.
     */
    fun goBack() {
        port?.goBack()
    }

    fun goForward() {
        port?.goForward()
    }

    fun reload() {
        port?.reload()
    }

    /** Named `stopLoading` rather than `stop` so it never reads as closing the browser. */
    fun stopLoading() {
        port?.stop()
    }

    fun handleOpen(epoch: Long, commandId: Long, rawUrl: String): BrowserCommandEffect {
        val address = when (val evaluated = urlPolicy.evaluate(rawUrl)) {
            is BrowserUrlResult.Rejected -> {
                pageState = stateReducer.reduce(
                    pageState,
                    BrowserStateEvent.Failed(epoch, pageState.navigationId, BrowserFailure.BLOCKED_URL),
                )
                publish(pageState)
                return BrowserCommandEffect.Rejected(BrowserCommandRejection.INVALID_IDENTIFIER)
            }
            is BrowserUrlResult.Accepted -> evaluated.url
        }
        val transition = commandReducer.reduce(commandState, BrowserCommand.Open(epoch, commandId, address))
        commandState = transition.state
        return applyCommandEffect(transition.effect)
    }

    fun handleNavigate(epoch: Long, commandId: Long, rawUrl: String): BrowserCommandEffect {
        val address = when (val evaluated = urlPolicy.evaluate(rawUrl)) {
            is BrowserUrlResult.Rejected -> {
                pageState = stateReducer.reduce(
                    pageState,
                    BrowserStateEvent.Failed(epoch, pageState.navigationId, BrowserFailure.BLOCKED_URL),
                )
                publish(pageState)
                return BrowserCommandEffect.Rejected(BrowserCommandRejection.INVALID_IDENTIFIER)
            }
            is BrowserUrlResult.Accepted -> evaluated.url
        }
        val transition = commandReducer.reduce(commandState, BrowserCommand.Navigate(epoch, commandId, address))
        commandState = transition.state
        return applyCommandEffect(transition.effect)
    }

    fun handleClose(epoch: Long, commandId: Long): BrowserCommandEffect {
        val transition = commandReducer.reduce(commandState, BrowserCommand.Close(epoch, commandId))
        commandState = transition.state
        return applyCommandEffect(transition.effect)
    }

    /**
     * Cleans up after the host's session ends without closing the browser.
     *
     * Called when the transport dies for any reason other than a CLOSE — the desktop quitting, the
     * laptop sleeping, the network dropping. Everything the session owned goes with it: the
     * surface claim, the page state the television is showing, and any navigation still waiting for
     * a view to arrive. Skipping this leaves the next session unable to open anything at all.
     */
    fun handleSessionEnded() {
        val transition = commandReducer.abandon(commandState)
        commandState = transition.state
        pendingNavigation = null
        pageState = BrowserState()
        publish(pageState)
        // The view is not torn down from here. The Activity owns it, and leaving the browser
        // surface is what releases it — calling `close` would also fire a Closed event carrying
        // command identifiers no host ever sent.
    }

    /**
     * A newly authenticated Windows TLS session starts its command ids at 1 again. The television
     * must clear the previous host's watermark or the first tab/view clicks are refused as
     * STALE_COMMAND while the glass stays on "PC CONNECTED" (2026-09-08).
     */
    fun resetHostCommandWatermark() {
        commandState = commandState.copy(lastCommandId = 0)
        if (pageState.epoch != null && pageState.lastAcceptedCommandId != 0L) {
            pageState = pageState.copy(lastAcceptedCommandId = 0)
            publish(pageState)
        }
    }

    fun onStateEvent(event: BrowserStateEvent) {
        pageState = stateReducer.reduce(pageState, event)
        publish(pageState)
    }

    fun snapshot(): BrowserState = pageState

    /**
     * Makes a saved tab the session's foreground page without changing command ordering.
     *
     * Only a state from the active epoch, at or behind the session command watermark, can be
     * restored. That prevents a stale renderer (or a programming error in the tab layer) from
     * moving the secure session's monotonic identifiers forwards or across epochs.
     */
    fun activatePage(snapshot: BrowserState): Boolean {
        if (commandState.surface != BrowserSurfaceOwner.BROWSER ||
            snapshot.epoch != commandState.activeEpoch ||
            snapshot.lastAcceptedCommandId > commandState.lastCommandId
        ) {
            return false
        }
        pageState = snapshot
        publish(pageState)
        return true
    }

    /** Starts a local new-tab surface without issuing any network request. */
    fun handleOpenBlank(epoch: Long, commandId: Long): BrowserCommandEffect {
        val transition = commandReducer.reduce(commandState, BrowserCommand.OpenBlank(epoch, commandId))
        commandState = transition.state
        return applyCommandEffect(transition.effect)
    }

    /** Accepts a tab/view/library command on the same monotonic stream as navigation. */
    fun handleControl(epoch: Long, commandId: Long): BrowserCommandEffect {
        val transition = commandReducer.reduce(commandState, BrowserCommand.Control(epoch, commandId))
        commandState = transition.state
        if (transition.effect is BrowserCommandEffect.Control) {
            pageState = stateReducer.reduce(
                pageState,
                BrowserStateEvent.ControlAccepted(epoch, commandId),
            )
            publish(pageState)
        }
        return transition.effect
    }

    /** Clears the visible page after changing profile while retaining the active secure epoch. */
    fun handleResetBlank(epoch: Long, commandId: Long): BrowserCommandEffect {
        val transition = commandReducer.reduce(commandState, BrowserCommand.ResetBlank(epoch, commandId))
        commandState = transition.state
        return applyCommandEffect(transition.effect)
    }

    private fun applyCommandEffect(effect: BrowserCommandEffect): BrowserCommandEffect {
        when (effect) {
            is BrowserCommandEffect.Open -> {
                pageState = stateReducer.reduce(
                    pageState,
                    BrowserStateEvent.OpenAccepted(effect.epoch, effect.commandId, effect.address),
                )
                publish(pageState)
                val attached = port
                if (attached != null) {
                    attached.open(effect.epoch, effect.commandId, effect.address)
                } else {
                    pendingNavigation = Triple(effect.epoch, effect.commandId, effect.address)
                }
            }
            is BrowserCommandEffect.Navigate -> {
                pageState = stateReducer.reduce(
                    pageState,
                    BrowserStateEvent.NavigationAccepted(effect.epoch, effect.commandId, effect.address),
                )
                publish(pageState)
                val attached = port
                if (attached != null) {
                    attached.navigate(effect.epoch, effect.commandId, effect.address)
                } else {
                    // Held for the same reason as an open: a navigate can land in the gap between
                    // the surface being asked for and the view existing.
                    pendingNavigation = Triple(effect.epoch, effect.commandId, effect.address)
                }
            }
            is BrowserCommandEffect.Close -> {
                pageState = stateReducer.reduce(
                    pageState,
                    BrowserStateEvent.CloseAccepted(effect.epoch, effect.commandId),
                )
                publish(pageState)
                port?.close(effect.epoch, effect.commandId)
            }
            is BrowserCommandEffect.OpenBlank -> {
                pendingNavigation = null
                pageState = stateReducer.reduce(
                    pageState,
                    BrowserStateEvent.BlankOpened(effect.epoch, effect.commandId),
                )
                publish(pageState)
            }
            is BrowserCommandEffect.Control -> Unit
            is BrowserCommandEffect.ResetBlank -> {
                pendingNavigation = null
                pageState = stateReducer.reduce(
                    pageState,
                    BrowserStateEvent.BlankReset(effect.epoch, effect.commandId),
                )
                publish(pageState)
            }
            else -> publish(pageState)
        }
        return effect
    }
}
