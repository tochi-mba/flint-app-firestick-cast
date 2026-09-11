package com.rextechnologies.flint.mobile

import android.app.Activity
import android.content.Context
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.MobileCapabilityAssessor
import com.rextechnologies.flint.castcore.capability.NetworkPath
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.copy.MobileTab
import com.rextechnologies.flint.castcore.copy.PairingCopy
import com.rextechnologies.flint.mobile.net.DiscoveryRunner
import com.rextechnologies.flint.mobile.platform.AndroidNetworkWatcher
import com.rextechnologies.flint.mobile.platform.PhoneProbes
import com.rextechnologies.flint.mobile.platform.ReceiverPackage
import com.rextechnologies.flint.mobile.platform.TokenStore
import com.rextechnologies.flint.protocol.wire.CodecId
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * The one object that holds the app's state.
 *
 * Not an `androidx.lifecycle.ViewModel`. The activity declares every configuration change it handles
 * and so is never recreated underneath a running cast — which is the reason the declaration is there
 * — so the survival a ViewModel buys is survival from something that does not happen here, and the
 * dependency would be carried for nothing.
 */
class MobileController(
    context: Context,
    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate),
) {
    private val applicationContext = context.applicationContext
    private val watcher = AndroidNetworkWatcher(applicationContext)
    private val discovery = DiscoveryRunner(applicationContext)
    private val tokens = TokenStore(applicationContext)

    private val mutable = MutableStateFlow(MobileUiState())
    val state: StateFlow<MobileUiState> = mutable

    private var encoders: Set<CodecId> = emptySet()
    private var encoderProbe: ProbeOutcome = ProbeOutcome.NOT_PROBED
    private var virtualDisplayProbe: ProbeOutcome = ProbeOutcome.NOT_PROBED

    private var screenWidth = 1080
    private var screenHeight = 1920
    private var densityDpi = 420
    private var deviceName = "This phone"

    fun start(
        deviceName: String,
        screenWidth: Int,
        screenHeight: Int,
        densityDpi: Int,
    ) {
        this.deviceName = deviceName.ifBlank { "This phone" }
        this.screenWidth = screenWidth.coerceAtLeast(1)
        this.screenHeight = screenHeight.coerceAtLeast(1)
        this.densityDpi = densityDpi.coerceAtLeast(1)

        watcher.start()
        scope.launch {
            watcher.localNetwork.collect { network ->
                mutable.value = mutable.value.copy(network = network)
                reassess()
            }
        }
        mutable.value = mutable.value.copy(
            bundledReceiver = ReceiverPackage.bundled(applicationContext),
        )
    }

    fun stop() {
        watcher.stop()
        scope.cancel()
    }

    /** Re-reads the interface list now, without running a discovery sweep. */
    fun refreshNetwork() {
        watcher.resample()
    }

    fun selectTab(tab: MobileTab) {
        mutable.value = mutable.value.copy(tab = tab)
    }

    fun dismissNotice() {
        mutable.value = mutable.value.copy(notice = null, sessionFailure = null, pairingError = null)
    }

    fun markIntroductionSeen() {
        mutable.value = mutable.value.copy(introductionSeen = true)
    }

    /** Runs the ladder, then measures the path against whatever answered. */
    fun probe() {
        if (mutable.value.isProbing) return
        mutable.value = mutable.value.copy(isProbing = true, notice = null)
        scope.launch {
            watcher.resample()
            val network = watcher.localNetwork.value
            val results = withContext(Dispatchers.IO) { discovery.discover(network) }
            val found = results.flatMap { it.receivers }.distinctBy { it.address }
            val selected = found.firstOrNull() ?: mutable.value.selected

            val path = selected?.let { measurePath(network, it) }
            mutable.value = mutable.value.copy(
                isProbing = false,
                network = network,
                receivers = found,
                selected = selected,
                rungsAttempted = results.filter { it.attempted }.map { it.rung },
                path = path,
            )
            reassess()
        }
    }

    /**
     * The round trip, measured by this phone rather than taken from the television.
     *
     * The receiver reports zero in every STATS frame because it never measures one, and a zero on a
     * diagnostics row reads as an extraordinarily good link rather than as no measurement at all.
     * Throughput is left unmeasured here, and reported as unmeasured, because measuring it properly
     * needs a receiver willing to sink traffic and this probe is not that.
     */
    private suspend fun measurePath(network: LocalNetwork, device: ReceiverDevice): NetworkPath? {
        val samples = discovery.measureRoundTripMillis(network, device)
        if (samples.isEmpty()) return null
        val median = samples.sorted()[samples.size / 2]
        val jitter = samples.maxOrNull()!! - samples.minOrNull()!!
        val loss = (1.0 - samples.size.toDouble() / 5.0) * 100.0
        return NetworkPath(
            roundTripMs = median,
            jitterMs = jitter,
            throughputMbps = 0.0,
            packetLossPercent = loss.coerceIn(0.0, 100.0),
            throughputMeasured = false,
        )
    }

    /** Confirms one typed-in address. Discovery is never the only route to a television. */
    fun probeManualAddress(address: String) {
        scope.launch {
            val network = watcher.localNetwork.value
            val found = withContext(Dispatchers.IO) { discovery.probeManual(network, address.trim()) }
            if (found == null) {
                mutable.value = mutable.value.copy(
                    notice = "Nothing answered at ${address.trim()}. Check the address on the TV " +
                        "under Settings, My Fire TV, About, Network.",
                )
                return@launch
            }
            mutable.value = mutable.value.copy(
                receivers = (mutable.value.receivers + found).distinctBy { it.address },
                selected = found,
                path = measurePath(network, found),
            )
            reassess()
        }
    }

    fun select(device: ReceiverDevice) {
        mutable.value = mutable.value.copy(selected = device)
        reassess()
    }

    fun showPairing(visible: Boolean) {
        mutable.value = mutable.value.copy(pairingVisible = visible, pairingError = null)
    }

    fun submitPairingCode(code: String) {
        if (!PairingCopy.isWellFormed(code)) {
            mutable.value = mutable.value.copy(pairingError = PairingCopy.INVALID_CODE)
            return
        }
        mutable.value = mutable.value.copy(pairingError = null)
        // Holding the session open is the next slice's work. What exists today is the verdict, the
        // discovery ladder and the state machine underneath them; claiming a pairing succeeded here
        // would be exactly the kind of control that fails when pressed.
        mutable.value = mutable.value.copy(
            pairingVisible = false,
            notice = "Pairing is not wired to the socket in this build yet, so nothing was sent.",
        )
    }

    /** Asks the platform what it can encode. Until this runs, the verdicts say it has not been asked. */
    fun runEncoderProbe() {
        scope.launch {
            val found = withContext(Dispatchers.Default) { PhoneProbes.probeHardwareEncoders() }
            encoders = found
            encoderProbe = if (found.isEmpty()) ProbeOutcome.UNSUPPORTED else ProbeOutcome.SUPPORTED
            reassess()
        }
    }

    /**
     * The second-screen check.
     *
     * It runs on the main thread because it puts a Presentation on a display, and a Dialog belongs to
     * the thread that made it. It takes about a second at worst, bounded by its own timeout.
     */
    fun runSecondScreenProbe(activity: Activity) {
        scope.launch {
            virtualDisplayProbe = PhoneProbes.probeVirtualDisplay(activity)
            reassess()
        }
    }

    private fun reassess() {
        val capabilities = PhoneProbes.capabilities(
            context = applicationContext,
            deviceName = deviceName,
            screenWidth = screenWidth,
            screenHeight = screenHeight,
            densityDpi = densityDpi,
            encoders = encoders,
            encoderProbe = encoderProbe,
            virtualDisplayProbe = virtualDisplayProbe,
        )
        val current = mutable.value
        val paired = current.selected?.let { tokens.tokenFor(it.address) != null } ?: false
        mutable.value = current.copy(
            isPaired = paired,
            report = MobileCapabilityAssessor.assess(
                network = current.network,
                phone = capabilities,
                device = current.selected,
                path = current.path,
                pairedSessionActive = paired,
            ),
        )
    }

}
