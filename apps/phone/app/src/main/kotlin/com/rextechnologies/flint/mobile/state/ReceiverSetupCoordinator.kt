package com.rextechnologies.flint.mobile.state

import android.content.Context
import com.rextechnologies.flint.castcore.capability.AdbConnectionState
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.setup.BundledReceiver
import com.rextechnologies.flint.castcore.setup.InstallEvent
import com.rextechnologies.flint.castcore.setup.InstallFlow
import com.rextechnologies.flint.castcore.setup.ReceiverInstallStage
import com.rextechnologies.flint.mobile.net.AdbAnswer
import com.rextechnologies.flint.mobile.net.ReceiverInstaller
import com.rextechnologies.flint.mobile.platform.ReceiverPackage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** What this build carries for the television, and how far setting it up has got. */
data class ReceiverSetupState(
    val bundled: BundledReceiver? = null,
    val stage: ReceiverInstallStage = ReceiverInstallStage.Unknown,
    /** The television [stage] is about, or `null` before any has been asked. */
    val address: String? = null,
    /** The receiver package the television listed when last asked, or `null`. */
    val installedPackage: String? = null,
    /** Whether the phone is talking to the television right now. One conversation at a time. */
    val busy: Boolean = false,
)

/** What one look at a television produced: the device as it is now known, and the raw answer. */
data class Identification(val device: ReceiverDevice, val answer: AdbAnswer)

/**
 * The receiver package this build carries, and the work of putting it on a television.
 *
 * Reading the package is several megabytes of file copy the first time, so it happens once, off the
 * main thread, and the card says "no receiver is bundled" until it has -- which is what is true
 * before it has been looked at, and is also what is true for every local and pull-request build.
 *
 * Every stage transition goes through [InstallFlow], so what the card can say is a table with a
 * test rather than a consequence of which method ran last. What ADB learned about the television
 * itself -- the platform, the model, the port -- is returned to the caller rather than kept here,
 * because the device list is discovery's to own.
 */
class ReceiverSetupCoordinator(
    private val installer: ReceiverInstaller,
    private val scope: CoroutineScope,
    private val bundledPackage: suspend () -> BundledReceiver?,
    private val stagedApk: suspend () -> ByteArray?,
) {
    constructor(context: Context, scope: CoroutineScope, installer: ReceiverInstaller) : this(
        installer = installer,
        scope = scope,
        bundledPackage = { ReceiverPackage.bundled(context.applicationContext) },
        stagedApk = {
            withContext(Dispatchers.IO) {
                runCatching { ReceiverPackage.stage(context.applicationContext)?.readBytes() }.getOrNull()
            }
        },
    )

    private val mutable = MutableStateFlow(ReceiverSetupState())
    val state: StateFlow<ReceiverSetupState> = mutable

    @Volatile
    private var loading = false

    /** Reads the bundled package. Idempotent: a second call while the first is running does nothing. */
    fun load() {
        if (loading || mutable.value.bundled != null) return
        loading = true
        scope.launch {
            val bundled = bundledPackage()
            mutable.update { it.copy(bundled = bundled) }
            loading = false
        }
    }

    /**
     * Asks the television what it is and whether Flint is on it. Read-only.
     *
     * The first look at a television shows its authorisation prompt; that is the television's
     * owner being asked, and the stage waits on them rather than on anything the phone can do.
     */
    suspend fun identify(network: LocalNetwork, device: ReceiverDevice): Identification {
        if (!begin(device)) return Identification(device, AdbAnswer.Failed(0, BUSY))
        try {
            val answer = installer.identify(network, device.address, device.adbPort, packagesToAskFor())
            val updated = device.updatedBy(answer)
            reduce(eventFor(answer))
            if (answer is AdbAnswer.Identified) mutable.update { it.copy(installedPackage = answer.installedPackage) }
            return Identification(updated, answer)
        } finally {
            end()
        }
    }

    /** Pushes the bundled package to the television and opens it. */
    suspend fun install(network: LocalNetwork, device: ReceiverDevice): InstallEvent {
        val bundled = mutable.value.bundled
            ?: return InstallEvent.InstallFailed("No receiver is bundled with this build.")
        if (!begin(device)) return InstallEvent.InstallFailed(BUSY)
        try {
            val apk = stagedApk()
            if (apk == null || apk.isEmpty()) {
                return reduce(InstallEvent.InstallFailed("The bundled package could not be read from this app."))
            }
            reduce(InstallEvent.InstallStarted)
            val event = installer.install(network, device.address, device.adbPort, apk, bundled.packageName)
            if (event ==
                InstallEvent.InstallSucceeded
            ) {
                mutable.update { it.copy(installedPackage = bundled.packageName) }
            }
            return reduce(event)
        } finally {
            end()
        }
    }

    /** Removes whichever Flint package the television was last seen to have. */
    suspend fun remove(network: LocalNetwork, device: ReceiverDevice): InstallEvent {
        val packageName = mutable.value.installedPackage
            ?: return InstallEvent.RemoveFailed(
                "Flint has not seen which package is on this TV. Check the TV again first.",
            )
        if (!begin(device)) return InstallEvent.RemoveFailed(BUSY)
        try {
            val event = installer.remove(network, device.address, device.adbPort, packageName)
            if (event == InstallEvent.RemoveSucceeded) mutable.update { it.copy(installedPackage = null) }
            return reduce(event)
        } finally {
            end()
        }
    }

    /** Claims the one conversation, and points the card at [device]. */
    private fun begin(device: ReceiverDevice): Boolean {
        var claimed = false
        mutable.update { current ->
            if (current.busy) return@update current
            claimed = true
            // A different television is a different question. What was known about the last one
            // is not evidence about this one.
            val stage = if (current.address == device.address) current.stage else ReceiverInstallStage.Unknown
            val installed = if (current.address == device.address) current.installedPackage else null
            current.copy(busy = true, address = device.address, stage = stage, installedPackage = installed)
        }
        return claimed
    }

    private fun end() = mutable.update { it.copy(busy = false) }

    private fun reduce(event: InstallEvent): InstallEvent {
        mutable.update { it.copy(stage = InstallFlow.reduce(it.stage, event)) }
        return event
    }

    /** The bundled package first, then every name a receiver has shipped under. */
    private fun packagesToAskFor(): Set<String> =
        listOfNotNull(mutable.value.bundled?.packageName).toSet() + BundledReceiver.KNOWN_PACKAGES

    private companion object {
        const val BUSY = "The phone is already talking to the TV."

        fun eventFor(answer: AdbAnswer): InstallEvent = when (answer) {
            is AdbAnswer.Identified ->
                if (answer.platform == ReceiverPlatform.VEGA) {
                    InstallEvent.NotAndroid
                } else {
                    InstallEvent.Identified(answer.receiverInstalled)
                }

            is AdbAnswer.Unauthorised -> InstallEvent.AuthorisationRequired
            is AdbAnswer.Refused -> InstallEvent.Unreachable(answer.detail)
            is AdbAnswer.Silent -> InstallEvent.Unreachable(answer.detail)
            is AdbAnswer.Failed -> InstallEvent.Unreachable(answer.detail)
        }

        /** The device with what the television said about itself, and nothing it did not say. */
        fun ReceiverDevice.updatedBy(answer: AdbAnswer): ReceiverDevice = when (answer) {
            is AdbAnswer.Identified -> copy(
                platform = answer.platform,
                adbState = AdbConnectionState.CONNECTED,
                model = answer.model.ifBlank { model },
                androidRelease = answer.androidRelease.ifBlank { androidRelease },
                androidApiLevel = answer.apiLevel ?: androidApiLevel,
                adbPort = answer.port,
            )

            is AdbAnswer.Unauthorised -> copy(adbState = AdbConnectionState.UNAUTHORIZED, adbPort = answer.port)
            is AdbAnswer.Refused -> copy(adbState = AdbConnectionState.REFUSED)
            is AdbAnswer.Silent -> copy(adbState = AdbConnectionState.TIMED_OUT)
            is AdbAnswer.Failed -> copy(adbState = AdbConnectionState.NOT_PROBED, adbPort = answer.port)
        }
    }
}
