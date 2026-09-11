package com.rextechnologies.flint.mobile.state

import android.content.Context
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.castcore.setup.ReceiverInstallStage
import com.rextechnologies.flint.mobile.platform.ReceiverPackage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** What this build carries for the television, and how far setting it up has got. */
data class ReceiverSetupState(
    val bundled: BundledReceiver? = null,
    val stage: ReceiverInstallStage = ReceiverInstallStage.Unknown,
)

/**
 * The receiver package this build carries, and the stage of putting it on a television.
 *
 * Reading the package is several megabytes of file copy the first time, so it happens once, off the
 * main thread, and the card says "no receiver is bundled" until it has — which is what is true
 * before it has been looked at, and is also what is true for every local and pull-request build.
 *
 * Installing over ADB is slice 10's work and is not here. The stage stays at what is actually known
 * rather than being driven from a flow that does not exist, and the setup card's install control
 * stays disabled with its reason on the card, because a control that fails when pressed is exactly
 * what the capability verdicts exist to prevent.
 */
class ReceiverSetupCoordinator(
    context: Context,
    private val scope: CoroutineScope,
) {
    private val applicationContext = context.applicationContext

    private val mutable = MutableStateFlow(ReceiverSetupState())
    val state: StateFlow<ReceiverSetupState> = mutable

    /** Reads the bundled package. Idempotent: a second call while the first is running does nothing. */
    fun load() {
        if (loading || mutable.value.bundled != null) return
        loading = true
        scope.launch {
            val bundled = ReceiverPackage.bundled(applicationContext)
            mutable.update { it.copy(bundled = bundled) }
            loading = false
        }
    }

    /** Records what an install attempt reached, for whenever one becomes possible. */
    fun stage(stage: ReceiverInstallStage) {
        mutable.update { it.copy(stage = stage) }
    }

    @Volatile
    private var loading = false
}
