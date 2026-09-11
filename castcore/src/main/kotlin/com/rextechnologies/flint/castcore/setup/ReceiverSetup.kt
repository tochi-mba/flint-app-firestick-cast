package com.rextechnologies.flint.castcore.setup

import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.capability.canInstallReceiver
import com.rextechnologies.flint.protocol.text.Decimal

/** Where receiver setup has got to on one television. */
enum class ReceiverInstallStage {
    /** Nothing has been attempted, or the television has not been identified. */
    UNKNOWN,

    /** Android is there and Flint is not. */
    NOT_INSTALLED,

    /** The television is showing its own authorisation prompt and is waiting for somebody to accept it. */
    AWAITING_AUTHORISATION,

    /** The television has accepted this phone's key. Nothing has been installed yet. */
    AUTHORISED,

    /** The package is being pushed and installed. */
    INSTALLING,

    /** Flint is installed and this phone put it there. */
    INSTALLED,

    /** The attempt failed and the reason is worth showing. */
    FAILED,

    /** This device can never run a receiver. */
    IMPOSSIBLE,
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

    private companion object {
        /**
         * Binary megabytes, because that is what the phone's own package manager reports and a
         * person comparing the two numbers should see them agree.
         */
        const val BYTES_PER_MEGABYTE = 1024.0 * 1024.0
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
) {
    init {
        require(headline.isNotBlank() && body.isNotBlank())
        require(installAction == null || disclosure.isNotEmpty()) {
            "Nothing is offered for installation without saying what it is"
        }
        require(stage != ReceiverInstallStage.IMPOSSIBLE || remedy == null) {
            "An impossibility cannot carry a remedy"
        }
    }
}

/** Builds the setup screen's contents from what is actually known. */
object ReceiverSetup {
    const val INSTALL_ACTION: String = "Install on this TV"
    const val REMOVE_ACTION: String = "Remove from this TV"
    const val RETRY_ACTION: String = "Try again"

    fun plan(
        platform: ReceiverPlatform,
        stage: ReceiverInstallStage,
        bundled: BundledReceiver?,
        deviceName: String,
        failureDetail: String = "",
    ): ReceiverSetupPlan {
        if (platform == ReceiverPlatform.VEGA) {
            return ReceiverSetupPlan(
                stage = ReceiverInstallStage.IMPOSSIBLE,
                headline = "This TV cannot run Flint",
                body = "It runs Vega OS, which is not Android. An APK cannot be installed on it by " +
                    "any method, and no future version of Flint will change that.",
            )
        }

        if (!platform.canInstallReceiver() && stage != ReceiverInstallStage.INSTALLED) {
            return ReceiverSetupPlan(
                stage = ReceiverInstallStage.UNKNOWN,
                headline = "Flint has not identified this TV yet",
                body = "It will not offer to install anything on a device it cannot name. Identifying " +
                    "it is read-only: Flint asks the TV what it is and changes nothing.",
                remedy = "Connect to the TV over ADB from the Cast screen, then come back here.",
            )
        }

        return when (stage) {
            ReceiverInstallStage.AWAITING_AUTHORISATION -> ReceiverSetupPlan(
                stage = stage,
                headline = "Waiting for the TV",
                body = "$deviceName is showing a prompt asking whether to allow this phone to connect. " +
                    "That prompt is the TV owner's say in what runs on it, so Flint waits for it " +
                    "rather than working around it.",
                remedy = "Pick up the TV remote and accept the prompt. If none appeared, disconnect " +
                    "and connect again to make it show.",
            )

            ReceiverInstallStage.INSTALLING -> ReceiverSetupPlan(
                stage = stage,
                headline = "Installing on $deviceName",
                body = "The package is being copied to the TV and installed. This takes a few moments " +
                    "and the TV may go dark while it happens.",
            )

            ReceiverInstallStage.INSTALLED -> ReceiverSetupPlan(
                stage = stage,
                headline = "Flint is installed on $deviceName",
                body = bundled?.let { installedBody(it) }
                    ?: "The receiver is installed and answering.",
                disclosure = bundled?.let { disclosureFor(it) }.orEmpty(),
                removeAction = REMOVE_ACTION,
            )

            ReceiverInstallStage.FAILED -> ReceiverSetupPlan(
                stage = stage,
                headline = "The install did not finish",
                body = failureDetail.ifBlank {
                    "The TV refused the package and did not say why."
                },
                disclosure = bundled?.let { disclosureFor(it) }.orEmpty(),
                installAction = bundled?.let { RETRY_ACTION },
                removeAction = REMOVE_ACTION,
                remedy = "Check that the TV is still awake and on this phone's hotspot, then try again.",
            )

            ReceiverInstallStage.UNKNOWN,
            ReceiverInstallStage.NOT_INSTALLED,
            ReceiverInstallStage.AUTHORISED,
            -> notInstalled(bundled, deviceName)

            ReceiverInstallStage.IMPOSSIBLE -> ReceiverSetupPlan(
                stage = stage,
                headline = "This TV cannot run Flint",
                body = "It is not an Android device, so there is nothing to install.",
            )
        }
    }

    private fun notInstalled(bundled: BundledReceiver?, deviceName: String): ReceiverSetupPlan {
        if (bundled == null) {
            return ReceiverSetupPlan(
                stage = ReceiverInstallStage.NOT_INSTALLED,
                headline = "No receiver is bundled with this build",
                body = "This copy of Flint was built without a Fire TV package inside it, so it has " +
                    "nothing to offer $deviceName. Pairing with a receiver that is already installed " +
                    "still works.",
                remedy = "Download Flint again from the GitHub release. Builds published there carry " +
                    "the matching receiver.",
            )
        }

        return ReceiverSetupPlan(
            stage = ReceiverInstallStage.NOT_INSTALLED,
            headline = "Install Flint on $deviceName",
            body = "Flint is not on this TV yet. This phone can install it over ADB — nothing is " +
                "downloaded, because the package is already inside this app and was built from the " +
                "same source.",
            disclosure = disclosureFor(bundled),
            installAction = INSTALL_ACTION,
            removeAction = REMOVE_ACTION,
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
