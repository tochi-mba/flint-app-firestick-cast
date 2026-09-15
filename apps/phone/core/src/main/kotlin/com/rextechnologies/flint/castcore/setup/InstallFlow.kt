package com.rextechnologies.flint.castcore.setup

/** What happened on the way to a receiver being on the television, as the phone learned it. */
sealed interface InstallEvent {
    /** The television answered over ADB and said whether the receiver package is on it. */
    data class Identified(val receiverInstalled: Boolean) : InstallEvent

    /** The television is showing its own prompt and has not accepted this phone's key yet. */
    data object AuthorisationRequired : InstallEvent

    /** The device is Vega OS, which cannot run a receiver by any method. */
    data object NotAndroid : InstallEvent

    data object InstallStarted : InstallEvent

    data object InstallSucceeded : InstallEvent

    data class InstallFailed(val detail: String) : InstallEvent

    data object RemoveSucceeded : InstallEvent

    data class RemoveFailed(val detail: String) : InstallEvent

    /** ADB could not be reached at all: refused, timed out, or the link dropped. */
    data class Unreachable(val detail: String) : InstallEvent
}

/**
 * The stage machine behind the receiver-setup card, as a table with a test.
 *
 * Two rules are structural rather than conventional. Nothing leaves [ReceiverInstallStage.Impossible]:
 * a Vega device does not become installable because a later probe was less sure. And nothing on the
 * phone moves [ReceiverInstallStage.AwaitingAuthorisation] forward except the television saying yes,
 * which arrives here as an [InstallEvent.Identified] once a later connection succeeds.
 */
object InstallFlow {
    fun reduce(stage: ReceiverInstallStage, event: InstallEvent): ReceiverInstallStage {
        if (stage == ReceiverInstallStage.Impossible) return stage
        return when (event) {
            InstallEvent.NotAndroid -> ReceiverInstallStage.Impossible
            is InstallEvent.Identified ->
                if (event.receiverInstalled) ReceiverInstallStage.Installed else ReceiverInstallStage.NotInstalled

            InstallEvent.AuthorisationRequired -> ReceiverInstallStage.AwaitingAuthorisation
            InstallEvent.InstallStarted -> ReceiverInstallStage.Installing
            InstallEvent.InstallSucceeded -> ReceiverInstallStage.Installed
            is InstallEvent.InstallFailed -> ReceiverInstallStage.Failed(event.detail)
            InstallEvent.RemoveSucceeded -> ReceiverInstallStage.NotInstalled
            is InstallEvent.RemoveFailed -> ReceiverInstallStage.Failed(event.detail)
            // The card keeps saying what it last knew. An unreachable television is the Cast tab's
            // problem to report; the install stage is not evidence about it either way.
            is InstallEvent.Unreachable -> when (stage) {
                ReceiverInstallStage.Installing -> ReceiverInstallStage.Failed(event.detail)
                else -> stage
            }
        }
    }
}
