package com.rextechnologies.flint.mobile

import android.app.Activity
import android.content.Context
import android.content.Intent
import com.rextechnologies.flint.castcore.capability.AssessmentInput
import com.rextechnologies.flint.castcore.capability.MobileCapabilityAssessor
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.PairingCopy
import com.rextechnologies.flint.castcore.copy.ScreenCopy
import com.rextechnologies.flint.castcore.copy.SettingsCopy
import com.rextechnologies.flint.castcore.media.CodecNames
import com.rextechnologies.flint.castcore.media.MediaState
import com.rextechnologies.flint.castcore.screen.ActiveSurface
import com.rextechnologies.flint.castcore.screen.SurfaceEffect
import com.rextechnologies.flint.castcore.screen.SurfaceReducer
import com.rextechnologies.flint.castcore.screen.SurfaceRequest
import com.rextechnologies.flint.castcore.screen.SurfaceTransition
import com.rextechnologies.flint.mobile.media.ContentMediaSource
import com.rextechnologies.flint.mobile.net.DiscoveryRunner
import com.rextechnologies.flint.mobile.platform.AndroidNetworkWatcher
import com.rextechnologies.flint.mobile.platform.ThermalWatch
import com.rextechnologies.flint.mobile.platform.TokenStore
import com.rextechnologies.flint.mobile.state.CapabilityCoordinator
import com.rextechnologies.flint.mobile.state.DiscoveryCoordinator
import com.rextechnologies.flint.mobile.state.LinkState
import com.rextechnologies.flint.mobile.state.LookupState
import com.rextechnologies.flint.mobile.state.MediaCoordinator
import com.rextechnologies.flint.mobile.state.NavigationState
import com.rextechnologies.flint.mobile.state.OutputCoordinator
import com.rextechnologies.flint.mobile.state.ReceiverSetupCoordinator
import com.rextechnologies.flint.mobile.state.ReceiverSetupState
import com.rextechnologies.flint.mobile.state.SessionCoordinator
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancelChildren
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.launch

/**
 * The one object the UI talks to.
 *
 * Not an `androidx.lifecycle.ViewModel`. The activity declares every configuration change it handles
 * and so is never recreated underneath a running cast — which is the reason the declaration is there
 * — so the survival a ViewModel buys is survival from something that does not happen here, and the
 * dependency would be carried for nothing.
 *
 * It holds no state of its own beyond navigation. Each coordinator owns one question and answers it
 * in its own flow; this combines those answers into [MobileUiState] and routes the presses. That
 * split is what removed the lost update the old single-state version had: a handler that read the
 * whole state, suspended for several seconds on a network sweep and then wrote the whole state back
 * silently discarded everything the network watcher had learned in between.
 */
class MobileController(
    context: Context,
    deviceName: String,
    screenWidth: Int,
    screenHeight: Int,
    densityDpi: Int,
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate),
) {
    private val applicationContext = context.applicationContext

    private val watcher = AndroidNetworkWatcher(applicationContext)
    private val navigation = NavigationState(applicationContext, scope)
    private val discovery = DiscoveryCoordinator(DiscoveryRunner(applicationContext))
    private val capability = CapabilityCoordinator(
        context = applicationContext,
        deviceName = deviceName.ifBlank { "This phone" },
        screenWidth = screenWidth.coerceAtLeast(1),
        screenHeight = screenHeight.coerceAtLeast(1),
        densityDpi = densityDpi.coerceAtLeast(1),
    )
    private val session = SessionCoordinator(TokenStore(applicationContext), scope)
    private val thermal = ThermalWatch(applicationContext)
    private val output = OutputCoordinator(applicationContext, session, scope, thermal.level)
    private val media = MediaCoordinator(session, ContentMediaSource(applicationContext), scope)
    private val setup = ReceiverSetupCoordinator(applicationContext, scope)

    /**
     * What this phone has asked the television to show. One value, every transition through the
     * reducer, so a second screen and a pushed file can never be sent at once.
     */
    private var surface: ActiveSurface = ActiveSurface.Idle

    /**
     * Work that belongs to one run of the app rather than to this object.
     *
     * Cancelled by [stop] so that a stop-and-start cycle works. Cancelling the scope itself, which
     * is what this used to do, left the controller permanently dead — every later launch was a
     * no-op, and nothing said so.
     */
    private val work = CoroutineScope(scope.coroutineContext + SupervisorJob(scope.coroutineContext[Job]))

    init {
        // What the output and media paths have to say when they stop on their own -- a dead socket,
        // an encoder that gave up, a cancelled send -- reaches the banner through here, because the
        // coordinators own no navigation.
        scope.launch { output.notices.collect { navigation.notice(it) } }
        scope.launch { media.notices.collect { navigation.notice(it) } }
        // An output that ended on its own -- the notification's Stop, a dead link -- is no longer
        // the surface, whatever this phone last asked for.
        scope.launch {
            output.state.collect { live ->
                if (live == null && (surface == ActiveSurface.Presentation || surface == ActiveSurface.Mirror)) {
                    surface = ActiveSurface.Idle
                }
            }
        }
        scope.launch {
            session.state.collect { link ->
                if (link !is LinkState.Connected && surface != ActiveSurface.Idle) {
                    media.reset()
                    surface = SurfaceReducer.transition(surface, SurfaceRequest.SessionLost).next
                }
            }
        }
    }

    val state: StateFlow<MobileUiState> = combine(
        navigation.state,
        watcher.localNetwork,
        discovery.state,
        capability.state,
        combine(session.state, output.state, setup.state, media.state, ::Live),
    ) { nav, network, discovered, probed, live ->
        val (link, sending, receiverSetup, playing) = live
        val phone = capability.capabilities(probed)
        MobileUiState(
            tab = nav.tab,
            introductionSeen = nav.introductionSeen,
            network = network,
            lookup = discovered.lookup,
            manualLookup = discovered.manualLookup,
            report = MobileCapabilityAssessor.assess(
                AssessmentInput(
                    network = network,
                    phone = phone,
                    device = discovered.selected,
                    path = discovered.path,
                    pairedSessionActive = link is LinkState.Connected,
                ),
            ).takeIf { probed.hasBeenAsked || discovered.lookup !is LookupState.Idle },
            receivers = discovered.receivers,
            selected = discovered.selected,
            rungsAttempted = discovered.rungsAttempted,
            path = discovered.path,
            link = link,
            pairingVisible = nav.pairingVisible,
            pairingError = nav.pairingError,
            encoderProbeRunning = probed.encoderProbeRunning,
            secondScreenProbeRunning = probed.secondScreenProbeRunning,
            output = sending,
            media = playing,
            bundledReceiver = receiverSetup.bundled,
            installStage = receiverSetup.stage,
            notice = nav.notice,
        )
    }.stateIn(scope, SharingStarted.Eagerly, MobileUiState())

    fun start() {
        watcher.start()
        thermal.start()
        setup.load()
    }

    /** Ends this run. [start] may be called again afterwards, and works. */
    fun stop() {
        watcher.stop()
        thermal.stop()
        scope.launch { output.stop() }
        session.close()
        work.coroutineContext.cancelChildren()
    }

    /** Releases everything for good, when the activity is finishing rather than pausing. */
    fun close() {
        stop()
        watcher.close()
    }

    /** Re-reads the interface list now, without running a discovery sweep. */
    fun refreshNetwork() {
        watcher.resample()
    }

    fun selectTab(tab: MobileTab) = navigation.selectTab(tab)

    fun onBack(): Boolean = navigation.onBack()

    fun dismissNotice() = navigation.dismissNotice()

    fun markIntroductionSeen() = navigation.markIntroductionSeen()

    fun replayIntroduction() = navigation.replayIntroduction()

    fun showPairing(visible: Boolean) = navigation.showPairing(visible)

    /** Runs the ladder, then measures the path against whatever answered. */
    fun probe() {
        launchWork {
            navigation.notice(null)
            watcher.resample()
            val outcome = discovery.probe(watcher.localNetwork.value)
            if (outcome is LookupState.Found) reconnectIfRemembered()
        }
    }

    /** Confirms one typed-in address. Discovery is never the only route to a television. */
    fun probeManualAddress(address: String) {
        launchWork {
            val found = discovery.probeManual(watcher.localNetwork.value, address)
            if (found == null) {
                navigation.notice(
                    "Nothing answered at ${address.trim()}. Check the address on the TV under " +
                        "Settings, My Fire TV, About, Network.",
                )
            } else {
                reconnectIfRemembered()
            }
        }
    }

    fun select(device: ReceiverDevice) {
        discovery.select(device)
        launchWork { reconnectIfRemembered() }
    }

    fun submitPairingCode(code: String) {
        val failure = session.pair(
            network = watcher.localNetwork.value,
            device = discovery.state.value.selected,
            phone = capability.capabilities(),
            code = code,
        )
        if (failure != null) {
            navigation.pairingError(failure)
            return
        }
        navigation.showPairing(false)
        launchWork {
            val settled = session.awaitSettled()
            navigation.notice(pairingOutcome(settled))
        }
    }

    /**
     * The encoder check: what the platform lists, then whether a test frame survives the encoder a
     * session would use. Needs the Activity because the frame is drawn through a Presentation.
     */
    fun runEncoderProbe(activity: Activity) {
        launchWork {
            val check = capability.probeEncoders(activity)
            navigation.notice(
                SettingsCopy.encoderCheckResult(
                    found = check.encoders.map(CodecNames::label),
                    through = check.through?.let(CodecNames::label),
                    roundTrip = check.roundTrip,
                    detail = check.detail,
                ),
            )
        }
    }

    /** The second-screen check, which needs an Activity because a Presentation is a Dialog. */
    fun runSecondScreenProbe(activity: Activity) {
        launchWork {
            navigation.notice(
                when (capability.probeSecondScreen(activity)) {
                    ProbeOutcome.SUPPORTED -> SettingsCopy.SECOND_SCREEN_PROBE_SUPPORTED
                    ProbeOutcome.UNSUPPORTED -> SettingsCopy.SECOND_SCREEN_PROBE_UNSUPPORTED
                    ProbeOutcome.NOT_PROBED -> null
                },
            )
        }
    }

    /** Starts the second screen, which needs no consent dialog and no projection. */
    fun startSecondScreen(activity: Activity) {
        launchWork {
            request(SurfaceRequest.StartPresentation)
            val failure = output.startSecondScreen(activity, capability.capabilities())
            if (failure != null) request(SurfaceRequest.StopOutput)
            navigation.notice(failure)
        }
    }

    /** Continues a mirror after the system's capture dialog has been answered. */
    fun onProjectionConsent(resultCode: Int, consent: Intent?) {
        if (consent == null) {
            navigation.notice(ScreenCopy.MIRROR_CONSENT_DECLINED)
            return
        }
        launchWork {
            request(SurfaceRequest.StartMirror)
            val failure = output.startMirror(capability.capabilities(), resultCode, consent)
            if (failure != null) request(SurfaceRequest.StopOutput)
            navigation.notice(failure)
        }
    }

    /** A file from the system picker, by its content URI. Never a path. */
    fun chooseVideo(reference: String) {
        launchWork {
            request(SurfaceRequest.StartMedia)
            if (!media.choose(reference)) request(SurfaceRequest.ClearMedia)
        }
    }

    fun playMedia() = media.play()

    fun pauseMedia() = media.pause()

    fun seekMedia(positionMs: Long) = media.seekTo(positionMs)

    fun stopMedia() = media.stop()

    fun cancelMediaTransfer() {
        launchWork {
            media.cancel()
            request(SurfaceRequest.ClearMedia)
        }
    }

    /** Returns the television to its idle screen, and says what it is not restarting. */
    fun clearMedia() {
        launchWork {
            val transition = request(SurfaceRequest.ClearMedia)
            // A failed transfer never became the surface, so the reducer had nothing to clear; the
            // television may still be holding a partial file, and is told to drop it either way.
            if (transition.effects.none { it is SurfaceEffect.ClearMedia }) media.clear()
        }
    }

    /**
     * Moves the surface through the reducer and carries out what it asked for.
     *
     * Effects run before the new value is taken, so a second screen is stopped before the LOAD that
     * displaces it goes out and the television never receives two surfaces at once.
     */
    private suspend fun request(request: SurfaceRequest): SurfaceTransition {
        val transition = SurfaceReducer.transition(surface, request)
        transition.effects.forEach { effect ->
            when (effect) {
                SurfaceEffect.StopOutput -> output.stop()
                SurfaceEffect.ClearMedia -> media.clear()
                is SurfaceEffect.Say -> navigation.notice(effect.sentence)
            }
        }
        surface = transition.next
        return transition
    }

    /** Whether a mirror may carry sound. The strip says why not when it may not. */
    fun onAudioPermission(granted: Boolean) {
        output.audioPermitted = granted
    }

    /** Says what happened when notifications were refused, and carries on regardless. */
    fun onNotificationPermission(granted: Boolean) {
        if (!granted) navigation.notice(ScreenCopy.NOTIFICATIONS_DECLINED)
    }

    fun stopOutput() {
        launchWork {
            request(SurfaceRequest.StopOutput)
            output.stop()
        }
    }

    /** The phone was turned. A live mirror follows it; a second screen, being a landscape canvas, does not. */
    fun onDisplayChanged(width: Int, height: Int, densityDpi: Int) {
        launchWork { output.reconfigureMirror(width, height, densityDpi) }
    }

    /** Ends the session with the television, leaving the stored pairing in place. */
    fun disconnect() {
        launchWork {
            output.stop()
            session.close()
        }
    }

    /** Unpairs every television, so a lent phone can be handed back. */
    fun forgetEveryPairing() {
        session.forgetEverything()
        navigation.notice(SettingsCopy.FORGOTTEN)
    }

    private suspend fun reconnectIfRemembered() {
        if (session.state.value is LinkState.Connected) return
        val device = discovery.state.value.selected ?: return
        session.reconnect(watcher.localNetwork.value, device, capability.capabilities())
    }

    private fun pairingOutcome(settled: LinkState): String? = when (settled) {
        is LinkState.Connected ->
            PairingCopy.paired(settled.device.displayName)

        is LinkState.Closed -> settled.failure?.sentence
        else -> PairingCopy.CONNECTING
    }

    private fun launchWork(block: suspend CoroutineScope.() -> Unit) {
        work.launch(block = block)
    }

    /** The four fast-changing answers, combined first because `combine` takes five flows at most. */
    private data class Live(
        val link: LinkState,
        val output: LiveOutput?,
        val setup: ReceiverSetupState,
        val media: MediaState,
    )
}
