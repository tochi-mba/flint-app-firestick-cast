package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.capability.canInstallReceiver
import com.rextechnologies.flint.castcore.capability.isTooOldForReceiver
import com.rextechnologies.flint.protocol.text.Decimal

/**
 * Where receiver setup has got to on one television.
 *
 * Sealed rather than an enum because one of the stages carries something: a failure that cannot say
 * what went wrong is not much of a failure report. That detail used to travel beside the stage as a
 * fifth parameter of [ReceiverSetup.plan], which every caller had to remember to pass and which was
 * meaningless for the other seven stages.
 */
sealed interface ReceiverInstallStage {
    /** Nothing has been attempted, or the television has not been identified. */
    data object Unknown : ReceiverInstallStage

    /** Android is there and Flint is not. */
    data object NotInstalled : ReceiverInstallStage

    /** The television is showing its own authorisation prompt and is waiting for somebody to accept it. */
    data object AwaitingAuthorisation : ReceiverInstallStage

    /** The television has accepted this phone's key. Nothing has been installed yet. */
    data object Authorised : ReceiverInstallStage

    /** The package is being pushed and installed. */
    data object Installing : ReceiverInstallStage

    /** Flint is installed and this phone put it there. */
    data object Installed : ReceiverInstallStage

    /**
     * The attempt failed.
     *
     * @property detail what the television said, verbatim where it said anything. Blank is allowed
     *   and is itself a fact -- a package manager that refuses without a reason is a thing that
     *   happens, and the copy has a sentence for it.
     */
    data class Failed(val detail: String = "") : ReceiverInstallStage

    /** This device can never run a receiver. */
    data object Impossible : ReceiverInstallStage
}

/**
 * The package this build of the phone app carries, read from the APK itself.
 *
 * Read rather than recorded at build time on purpose: a properties file written by a workflow says
 * what somebody intended to bundle, and the APK says what is actually there. Only one of those can be
 * shown to a person above the words "this is what will be installed".
 */
data class BundledReceiver(
    val packageName: String,
    val versionName: String,
    val versionCode: Long,
    val sizeBytes: Long,
) {
    init {
        require(packageName.isNotBlank())
        require(versionName.isNotBlank())
        require(versionCode >= 0)
        require(sizeBytes > 0)
    }

    /** Debug builds carry a suffixed package, and a person is entitled to know that before installing. */
    val isDebugPackage: Boolean
        get() = packageName.endsWith(".debug")

    /** The size a person sees above "this is what will be installed", in megabytes. */
    val sizeLabel: String
        get() = "${Decimal.oneDecimal(sizeBytes.toDouble() / BYTES_PER_MEGABYTE)} MB"

    companion object {
        /** The receiver as released, and as a local or pull-request build names it. */
        const val RELEASE_PACKAGE: String = "com.rextechnologies.flint.receiver"
        const val DEBUG_PACKAGE: String = "$RELEASE_PACKAGE.debug"

        /** Every package name a Flint receiver is known under, for asking a television which it has. */
        val KNOWN_PACKAGES: Set<String> = setOf(RELEASE_PACKAGE, DEBUG_PACKAGE)

        /** The activity to open once the package is installed, relative to its package. */
        const val MAIN_ACTIVITY: String = ".ReceiverActivity"

        /**
         * Binary megabytes, because that is what the phone's own package manager reports and a
         * person comparing the two numbers should see them agree.
         */
        private const val BYTES_PER_MEGABYTE = 1024.0 * 1024.0
    }
}

/**
 * What the receiver-setup screen says and offers.
 *
 * Three of this project's constraints meet on this one screen, and all three are structural here
 * rather than left to whoever writes the UI:
 *
 * - Nothing is installed, replaced or removed without showing what and why, so [disclosure] is never
 *   empty when [installAction] is offered.
 * - The television's own authorisation prompt is honoured rather than routed around, so
 *   [ReceiverInstallStage.AWAITING_AUTHORISATION] has no action on the phone at all: the only thing
 *   that moves it forward is somebody picking up the TV remote.
 * - Removal is offered wherever installation is offered, on the same screen.
 */
data class ReceiverSetupPlan(
    val stage: ReceiverInstallStage,
    val headline: String,
    val body: String,
    val disclosure: List<String> = emptyList(),
    val installAction: String? = null,
    val removeAction: String? = null,
    val remedy: String? = null,
    /** The read-only look at the television, offered wherever it would tell the card something new. */
    val identifyAction: String? = null,
) {
    init {
        require(headline.isNotBlank() && body.isNotBlank())
        require(installAction == null || disclosure.isNotEmpty()) {
            "Nothing is offered for installation without saying what it is"
        }
        require(stage != ReceiverInstallStage.Impossible || remedy == null) {
            "An impossibility cannot carry a remedy"
        }
    }
}

/** Builds the setup screen's contents from what is actually known. */
object ReceiverSetup {
    const val INSTALL_ACTION: String = "Install on this TV"
    const val REMOVE_ACTION: String = "Remove from this TV"
    const val RETRY_ACTION: String = "Try again"
    const val IDENTIFY_ACTION: String = "Identify this TV"
    const val RECHECK_ACTION: String = "Check the TV again"
    const val WORKING: String = "Talking to the TV…"
    const val CONFIRM_REMOVE_ACTION: String = "Yes, remove it"
    const val KEEP_ACTION: String = "Keep it"
    const val NOTHING_SELECTED: String =
        "No television is chosen. Find one on the Cast screen, or type its address there, and it " +
            "will appear here."

    /** The second press. It names the package and the television so nothing is removed by reflex. */
    fun removeConfirmation(packageName: String, deviceName: String): String =
        "Remove $packageName from $deviceName? The television will go back to whatever it showed " +
            "before Flint, and this phone can install it again from this card."

    /** What identifying found, for the banner. */
    fun identified(deviceName: String, platformLabel: String, receiverInstalled: Boolean): String {
        val receiver = if (receiverInstalled) "Flint is installed on it." else "Flint is not on it yet."
        return "$deviceName answered over ADB. It runs $platformLabel. $receiver"
    }

    fun unauthorised(deviceName: String): String =
        "$deviceName is showing a prompt asking whether to allow this phone. Accept it with the TV " +
            "remote, then check the TV again."

    fun notAndroid(deviceName: String): String =
        "$deviceName runs Vega OS, which is not Android. Flint cannot be installed on it by any method."

    fun installed(deviceName: String): String =
        "Flint is installed on $deviceName and should now appear as a receiver on the Cast screen."

    fun removed(deviceName: String): String = "Flint has been removed from $deviceName."

    fun failed(detail: String): String {
        val trimmed = detail.trim()
        if (trimmed.isEmpty()) return "The television refused, and did not say why."
        return if (trimmed.last() in SENTENCE_ENDINGS) trimmed else "$trimmed."
    }

    private val SENTENCE_ENDINGS = charArrayOf('.', '?', '…')

    fun plan(
        platform: ReceiverPlatform,
        stage: ReceiverInstallStage,
        bundled: BundledReceiver?,
        deviceName: String,
    ): ReceiverSetupPlan {
        if (platform == ReceiverPlatform.VEGA) {
            return ReceiverSetupPlan(
                stage = ReceiverInstallStage.Impossible,
                headline = "This TV cannot run Flint",
                body = "It runs Vega OS, which is not Android. An APK cannot be installed on it by " +
                    "any method, and no future version of Flint will change that.",
            )
        }

        // Android, and older than the receiver's manifest allows. Its own branch because the
        // "not identified yet" card below offers a remedy, and there is none for this: no setting on
        // the television and no future version of Flint can lower a package manager's floor.
        if (platform.isTooOldForReceiver() && stage != ReceiverInstallStage.Installed) {
            return ReceiverSetupPlan(
                stage = ReceiverInstallStage.Impossible,
                headline = "This TV is too old for Flint",
                body = "It runs ${platform.displayLabel}, which is Android but older than the Flint " +
                    "receiver can be installed on. The television's package manager refuses the app " +
                    "outright, and Amazon has never offered these devices a newer version.",
            )
        }

        if (!platform.canInstallReceiver() && stage != ReceiverInstallStage.Installed) {
            return ReceiverSetupPlan(
                stage = if (stage ==
                    ReceiverInstallStage.AwaitingAuthorisation
                ) {
                    stage
                } else {
                    ReceiverInstallStage.Unknown
                },
                headline = "Flint has not identified this TV yet",
                body = "It will not offer to install anything on a device it cannot name. Identifying " +
                    "it is read-only: Flint asks the TV what it is and changes nothing. The first " +
                    "time, the TV shows a prompt asking whether to allow this phone.",
                remedy = if (stage == ReceiverInstallStage.AwaitingAuthorisation) {
                    "Pick up the TV remote and accept the prompt, then check the TV again. If none " +
                        "appeared, the TV's ADB debugging may be off."
                } else {
                    "Choose the TV on the Cast screen, then identify it from this card. ADB debugging " +
                        "has to be on: Settings, My Fire TV, Developer Options."
                },
                identifyAction = if (stage ==
                    ReceiverInstallStage.AwaitingAuthorisation
                ) {
                    RECHECK_ACTION
                } else {
                    IDENTIFY_ACTION
                },
            )
        }

        return when (stage) {
            ReceiverInstallStage.AwaitingAuthorisation -> ReceiverSetupPlan(
                stage = stage,
                headline = "Waiting for the TV",
                body = "$deviceName is showing a prompt asking whether to allow this phone to connect. " +
                    "That prompt is the TV owner's say in what runs on it, so Flint waits for it " +
                    "rather than working around it.",
                remedy = "Pick up the TV remote and accept the prompt, then check the TV again. If " +
                    "none appeared, check the TV again to make it show.",
                identifyAction = RECHECK_ACTION,
            )

            ReceiverInstallStage.Installing -> ReceiverSetupPlan(
                stage = stage,
                headline = "Installing on $deviceName",
                body = "The package is being copied to the TV and installed. This takes a few moments " +
                    "and the TV may go dark while it happens.",
            )

            ReceiverInstallStage.Installed -> ReceiverSetupPlan(
                stage = stage,
                headline = "Flint is installed on $deviceName",
                body = bundled?.let { installedBody(it) }
                    ?: "The receiver is installed and answering.",
                disclosure = bundled?.let { disclosureFor(it) }.orEmpty(),
                removeAction = REMOVE_ACTION,
                identifyAction = RECHECK_ACTION,
            )

            is ReceiverInstallStage.Failed -> ReceiverSetupPlan(
                stage = stage,
                headline = "The install did not finish",
                body = stage.detail.ifBlank {
                    "The TV refused the package and did not say why."
                },
                disclosure = bundled?.let { disclosureFor(it) }.orEmpty(),
                installAction = bundled?.let { RETRY_ACTION },
                removeAction = REMOVE_ACTION,
                remedy = "Check that the TV is still awake and on this phone's hotspot, then try again.",
                identifyAction = RECHECK_ACTION,
            )

            ReceiverInstallStage.Unknown,
            ReceiverInstallStage.NotInstalled,
            ReceiverInstallStage.Authorised,
            -> notInstalled(bundled, deviceName)

            ReceiverInstallStage.Impossible -> ReceiverSetupPlan(
                stage = stage,
                headline = "This TV cannot run Flint",
                body = "It is not an Android device, so there is nothing to install.",
            )
        }
    }

    private fun notInstalled(bundled: BundledReceiver?, deviceName: String): ReceiverSetupPlan {
        if (bundled == null) {
            return ReceiverSetupPlan(
                stage = ReceiverInstallStage.NotInstalled,
                headline = "No receiver is bundled with this build",
                body = "This copy of Flint was built without a Fire TV package inside it, so it has " +
                    "nothing to offer $deviceName. Pairing with a receiver that is already installed " +
                    "still works.",
                remedy = "Download Flint again from the GitHub release. Builds published there carry " +
                    "the matching receiver.",
                identifyAction = RECHECK_ACTION,
            )
        }

        return ReceiverSetupPlan(
            stage = ReceiverInstallStage.NotInstalled,
            headline = "Install Flint on $deviceName",
            body = "Flint is not on this TV yet. This phone can install it over ADB — nothing is " +
                "downloaded, because the package is already inside this app and was built from the " +
                "same source.",
            disclosure = disclosureFor(bundled),
            installAction = INSTALL_ACTION,
            removeAction = REMOVE_ACTION,
            identifyAction = RECHECK_ACTION,
        )
    }

    private fun installedBody(bundled: BundledReceiver): String = buildString {
        append("Version ${bundled.versionName} is installed.")
        if (bundled.isDebugPackage) {
            append(
                " It is the debug build, whose package name ends in .debug — that is what the phone " +
                    "is able to install, because a TV refuses an unsigned package and the release " +
                    "signing key is not inside this app.",
            )
        }
    }

    /**
     * Exactly what will be installed, in the order somebody would ask it.
     *
     * The package name is first because it is the part a person can check afterwards, and it is the
     * part that differs between a debug and a release build.
     */
    private fun disclosureFor(bundled: BundledReceiver): List<String> = buildList {
        add("Package: ${bundled.packageName}")
        add("Version: ${bundled.versionName} (${bundled.versionCode})")
        add("Size: ${bundled.sizeLabel}")
        add("Source: bundled inside this app, built from the same commit")
        add("What it does: renders the cast on the TV and answers this phone on port 47855")
        if (bundled.isDebugPackage) {
            add("Note: a debug build, so its package name ends in .debug")
        }
    }
}
