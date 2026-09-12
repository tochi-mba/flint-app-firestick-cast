package com.rextechnologies.flint.castcore.capability

/** Every verdict Flint holds about one phone, one network and one television. */
data class CapabilityReport(
    val network: LocalNetwork,
    val phone: PhoneCapabilities,
    val device: ReceiverDevice?,
    val path: NetworkPath?,
    val verdicts: List<ModeVerdict>,
) {
    init {
        val modes = verdicts.map { it.mode }
        require(modes.toSet() == CastMode.entries.toSet() && modes.size == CastMode.entries.size) {
            "A report must judge every mode exactly once"
        }
    }

    operator fun get(mode: CastMode): ModeVerdict = verdicts.first { it.mode == mode }

    /** Whether anything at all can be started right now. Drives the empty state, not a button. */
    val anythingOfferable: Boolean
        get() = verdicts.any { it.isOfferable }
}

/**
 * Everything one assessment is made from.
 *
 * Five arguments travelled together through every function here and through the phone's own
 * controller, and each hop repeated the same five names. Grouping them means a new piece of evidence
 * is added in one place rather than in nine signatures, and it means no call site can pass the
 * network where the path belongs.
 */
data class AssessmentInput(
    val network: LocalNetwork,
    val phone: PhoneCapabilities,
    val device: ReceiverDevice?,
    val path: NetworkPath? = null,
    /** Whether a session is open right now, which is the strongest possible proof of reachability. */
    val pairedSessionActive: Boolean = false,
)

/**
 * Decides which [CastMode]s this phone, this network and this television support.
 *
 * Pure: same inputs, same verdicts, no I/O, no clock, no ambient state. Every sentence it returns is
 * traceable to evidence in its arguments, which is what makes the copy testable without a television
 * in the room.
 *
 * Verdict precedence is deliberate. A device that can never run a receiver outranks a probe that has
 * not been run, which outranks a network that is merely slow — because telling someone to go and
 * enable a setting on a television that could never have worked wastes the one thing Flint is
 * supposed to be saving them.
 *
 * The second screen is not judged as "mirror plus something". On Windows it is, because there it is
 * a mirror plus an indirect display driver. On Android the two modes need genuinely different things
 * — the second screen renders into a private display this app owns and needs no capture consent at
 * all — so a phone that refuses screen capture can still drive a second screen, and this assessor
 * has to be able to say so.
 */
object MobileCapabilityAssessor {
    fun assess(input: AssessmentInput): CapabilityReport = CapabilityReport(
        network = input.network,
        phone = input.phone,
        device = input.device,
        path = input.path,
        verdicts = listOf(
            assessMirror(input),
            assessSecondScreen(input),
            assessMediaHandoff(input),
        ),
    )

    private fun assessMirror(input: AssessmentInput): ModeVerdict {
        val mode = CastMode.MIRROR
        val phone = input.phone
        val path = input.path
        reachability(input)?.let { return it.about(mode) }
        encoder(phone)?.let { return it.about(mode) }

        if (!phone.screenCaptureConsentAvailable) {
            return ModeVerdict(
                mode,
                ModeStatus.IMPOSSIBLE,
                "This phone has no screen-capture service for Flint to ask permission from, so there " +
                    "is no way for it to see its own screen. A second screen does not need one and is " +
                    "judged separately.",
            )
        }

        pathLimits(mode, path)?.let { return it }

        return ModeVerdict(
            mode,
            ModeStatus.AVAILABLE,
            "This phone can encode its own screen and the television can decode it." +
                capacityCaveat(path) + isolationCaveat(input.network) + roundTripCaveat(phone) +
                " Android asks for capture permission every time a mirror starts, and will not " +
                "remember the answer.",
        )
    }

    private fun assessSecondScreen(input: AssessmentInput): ModeVerdict {
        val mode = CastMode.SECOND_SCREEN
        val phone = input.phone
        val path = input.path
        reachability(input)?.let { return it.about(mode) }
        encoder(phone)?.let { return it.about(mode) }

        when (phone.virtualDisplayProbe) {
            ProbeOutcome.NOT_PROBED -> return ModeVerdict(
                mode,
                ModeStatus.BLOCKED,
                "Flint has not checked whether this phone will give it a display only this app can " +
                    "see to draw the second screen into, so it cannot say whether the mode would " +
                    "work. It will not assume the answer from the Android version.",
                "Run the second-screen check in Settings. It creates a display only this app can see, " +
                    "draws one frame into it and reads that frame back. Nothing appears on the " +
                    "television and nothing is sent anywhere.",
            )

            ProbeOutcome.UNSUPPORTED -> return ModeVerdict(
                mode,
                ModeStatus.IMPOSSIBLE,
                "This phone refused to create the display this mode draws into, so Flint has nothing " +
                    "to render the second screen onto. Mirroring is judged separately and may still " +
                    "work.",
            )

            ProbeOutcome.SUPPORTED -> Unit
        }

        pathLimits(mode, path)?.let { return it }

        return ModeVerdict(
            mode,
            ModeStatus.AVAILABLE,
            "The television becomes a screen Flint draws on while the phone stays the controller." +
                capacityCaveat(path) + isolationCaveat(input.network) + " " + SECOND_SCREEN_BOUNDARY,
        )
    }

    private fun assessMediaHandoff(input: AssessmentInput): ModeVerdict {
        val mode = CastMode.MEDIA_HANDOFF
        val path = input.path
        reachability(input)?.let { return it.about(mode) }

        if (path != null && path.throughputMeasured &&
            path.throughputMbps < NetworkPath.MINIMUM_MEDIA_HANDOFF_MBPS
        ) {
            return ModeVerdict(
                mode,
                ModeStatus.BLOCKED,
                "The measured path carries ${path.throughputLabel}. Playing a file at its original " +
                    "quality needs at least " +
                    "${NetworkPath.wholeNumber(NetworkPath.MINIMUM_MEDIA_HANDOFF_MBPS)} Mbit/s, " +
                    "because nothing is re-encoded to make it fit.",
                "Move the television closer to the phone, or put the hotspot on 5 GHz if it offers " +
                    "the choice.",
            )
        }

        return ModeVerdict(
            mode,
            ModeStatus.AVAILABLE,
            "The television is reachable and the path is sufficient to play a file at its original " +
                "quality." + capacityCaveat(path) + isolationCaveat(input.network),
        )
    }

    /**
     * Whether anything about the network or the television stops Flint before the phone matters.
     *
     * The same answer for every mode that shares it, so it is decided once and asked about a mode by
     * whoever needed it. `null` when nothing here blocks.
     */
    private fun reachability(input: AssessmentInput): VerdictTemplate? {
        val network = input.network
        val device = input.device
        if (network is LocalNetwork.NoLocalNetwork) {
            return VerdictTemplate(
                ModeStatus.BLOCKED,
                "This phone is not on a local network, so there is nothing for it to find. Mobile " +
                    "data cannot carry a cast: the television has to be on the same link as the phone.",
                "Turn on this phone's hotspot and join the Fire TV to it, which is the arrangement " +
                    "Flint is built around. Joining both to the same Wi-Fi also works.",
            )
        }

        if (device == null) {
            return VerdictTemplate(
                ModeStatus.BLOCKED,
                "No television has answered on this network yet.",
                when (network) {
                    is LocalNetwork.PhoneIsHost ->
                        "Open the Fire TV's Wi-Fi settings and join it to this phone's hotspot, then " +
                            "probe again."

                    else ->
                        "Check that the Fire TV is awake and on this same network, then probe again. " +
                            "If the network keeps its clients from reaching each other, the probe will " +
                            "never arrive — this phone's own hotspot does not do that."
                },
            )
        }

        if (device.platform == ReceiverPlatform.VEGA) {
            return VerdictTemplate(
                ModeStatus.IMPOSSIBLE,
                "This device runs Vega OS, which is not Android and cannot install a receiver by any " +
                    "method. Flint cannot run on it, and no future version will change that.",
            )
        }

        // A Flint receiver answering its own port is stronger evidence than anything ADB can report:
        // it proves the receiver is installed, running and willing to talk. Keep blocking only while
        // that has not been shown.
        if (input.pairedSessionActive || device.receiverAnswered) return null

        if (device.platform.canInstallReceiver()) {
            return when (device.adbState) {
                AdbConnectionState.CONNECTED -> null

                AdbConnectionState.UNAUTHORIZED -> VerdictTemplate(
                    ModeStatus.BLOCKED,
                    "The television is reachable but has not authorised this phone.",
                    "Accept the authorisation prompt on the television. If none appeared, disconnect " +
                        "and connect again to make it show.",
                )

                else -> VerdictTemplate(
                    ModeStatus.BLOCKED,
                    "The television was identified but is not answering over ADB right now.",
                    "Check that it is awake and still on this network, then probe again.",
                )
            }
        }

        // Two different failures wear the same badge, and telling them apart is the whole value of
        // the message. A refusal means something is there and closed the port, which is genuinely
        // ambiguous between Vega and debugging switched off. A timeout means nothing answered, which
        // is overwhelmingly a wrong address — and sending someone into Developer Options on a
        // television that was never the problem wastes their time on the easiest failure to fix.
        return if (device.adbState == AdbConnectionState.REFUSED) {
            VerdictTemplate(
                ModeStatus.BLOCKED,
                "Something answered at ${device.address} but refused the ADB port. Either it is not " +
                    "an Android device, or ADB debugging is switched off. Flint cannot identify Fire " +
                    "OS or install the receiver until ADB answers.",
                "On the TV open Settings > My Fire TV > About, select the device name seven times, " +
                    "press Back, open Developer Options, enable ADB debugging, accept the " +
                    "authorisation prompt, then connect again.",
            )
        } else {
            VerdictTemplate(
                ModeStatus.BLOCKED,
                "Nothing answered at ${device.address}. The address may be wrong or stale, or the " +
                    "television may be asleep or on a different network.",
                "Check the address on the TV under Settings > My Fire TV > About > Network, and that " +
                    "it matches this network. If it does, wake the television and probe again.",
            )
        }
    }

    /**
     * The encoder gates both streaming modes share.
     *
     * Two questions, asked in order. Does the platform list a hardware encoder at all; and did a
     * test frame drawn through that encoder come back out of a decoder as the frame that was drawn.
     * The second is the one that matters, because the first has been answered "yes" by an encoder
     * that produced nothing but green. The round trip needs a display only this app can see to draw
     * into, so on a phone that refused one it cannot run, and that is reported as unchecked rather
     * than as failed: a mirror through a projection may still work, and the first mirror is then the
     * first proof.
     */
    private fun encoder(phone: PhoneCapabilities): VerdictTemplate? = when {
        phone.encoderProbe == ProbeOutcome.NOT_PROBED -> VerdictTemplate(
            ModeStatus.BLOCKED,
            "Flint has not asked this phone what video it can encode in hardware yet, so it cannot " +
                "say whether a stream is possible. It will not guess from the model name.",
            ENCODER_CHECK_REMEDY,
        )

        phone.encoderProbe == ProbeOutcome.UNSUPPORTED || phone.hardwareVideoEncoders.isEmpty() ->
            VerdictTemplate(
                ModeStatus.IMPOSSIBLE,
                "This phone reports no hardware video encoder. Encoding in software cannot meet the " +
                    "latency these modes exist to deliver, so Flint will not offer them.",
            )

        phone.encoderRoundTrip == ProbeOutcome.UNSUPPORTED -> VerdictTemplate(
            ModeStatus.IMPOSSIBLE,
            ENCODER_ROUND_TRIP_FAILED,
        )

        phone.encoderRoundTrip == ProbeOutcome.NOT_PROBED &&
            phone.virtualDisplayProbe != ProbeOutcome.UNSUPPORTED -> VerdictTemplate(
            ModeStatus.BLOCKED,
            ROUND_TRIP_NOT_RUN,
            ENCODER_CHECK_REMEDY,
        )

        else -> null
    }

    /** Said on an offered mirror when the round trip could not run, so "Ready" does not overstate. */
    private fun roundTripCaveat(phone: PhoneCapabilities): String =
        if (phone.encoderRoundTrip == ProbeOutcome.NOT_PROBED) " $ROUND_TRIP_UNCHECKED" else ""

    /** The measured-path gates both streaming modes share. */
    private fun pathLimits(mode: CastMode, path: NetworkPath?): ModeVerdict? {
        if (path == null) return null

        if (!path.latencySupportsMirroring) {
            return ModeVerdict(
                mode,
                ModeStatus.BLOCKED,
                "The measured round trip to the television is " +
                    "${NetworkPath.wholeNumber(path.roundTripMs)} ms. This needs it below " +
                    "${NetworkPath.wholeNumber(NetworkPath.USABLE_ROUND_TRIP_CEILING_MS)} ms to feel " +
                    "attached to the phone.",
                LINK_REMEDY,
            )
        }

        if (path.throughputTooLowForMirroring) {
            return ModeVerdict(
                mode,
                ModeStatus.BLOCKED,
                "The measured path carries ${path.throughputLabel}. This needs at least " +
                    "${NetworkPath.wholeNumber(NetworkPath.MINIMUM_MIRROR_THROUGHPUT_MBPS)} Mbit/s.",
                LINK_REMEDY,
            )
        }

        return null
    }

    private fun capacityCaveat(path: NetworkPath?): String =
        if (path != null && !path.throughputMeasured) {
            " Throughput has not been measured yet, so capacity is still unproven."
        } else {
            ""
        }

    private fun isolationCaveat(network: LocalNetwork): String = when (network) {
        is LocalNetwork.PhoneIsClient ->
            " Both are clients of a network somebody else runs, so if that network isolates its " +
                "clients from each other the session will not connect. Running the hotspot from this " +
                "phone avoids that entirely."

        else -> ""
    }

    /**
     * The one thing the second-screen card must never leave out.
     *
     * A `VirtualDisplay` shows only what the app that owns it draws. Flint cannot extend Android
     * itself onto the television and cannot put another app's window there, and a card that said
     * "second screen" without saying this would be describing the Windows feature rather than this
     * one.
     */
    const val SECOND_SCREEN_BOUNDARY: String =
        "The television shows Flint's own screens — a player, photos, what is playing now — not the " +
            "phone's home screen and not other apps. Android gives an app no way to move another " +
            "app's window onto a second display."

    const val ENCODER_ROUND_TRIP_FAILED: String =
        "A test frame drawn through this phone's encoder came back from a decoder as something " +
            "other than what was drawn, so anything it sent would reach the television as a wrong " +
            "or blank picture. Flint will not offer a stream it has watched fail."

    const val ROUND_TRIP_NOT_RUN: String =
        "This phone lists hardware encoders, but no test frame has been drawn through one and " +
            "decoded yet, so Flint cannot say whether its frames would reach the television as a " +
            "picture."

    const val ROUND_TRIP_UNCHECKED: String =
        "The encoder's output could not be checked on this phone, because it refused the display " +
            "the check draws into, so the first mirror is the first proof."

    const val ENCODER_CHECK_REMEDY: String =
        "Run the encoder check in Settings. It lists the hardware encoders, then encodes and " +
            "decodes one test frame on this phone. Nothing appears on the television and nothing " +
            "is sent anywhere."

    private const val LINK_REMEDY: String =
        "Move the television closer to the phone, or switch the hotspot to 5 GHz if it offers the " +
            "choice. Walls and 2.4 GHz congestion account for most of the failures here."
}
