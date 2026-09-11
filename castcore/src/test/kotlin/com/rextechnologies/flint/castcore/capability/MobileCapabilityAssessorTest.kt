package com.rextechnologies.flint.castcore.capability

import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import com.rextechnologies.flint.protocol.wire.CodecId
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

private fun address(value: String): Inet4Address = InetAddress.getByName(value) as Inet4Address

private val hostNetwork = LocalNetwork.PhoneIsHost(
    SelectedHotspotInterface("ap0", 7, address("192.168.43.1"), 24),
)

private val clientNetwork = LocalNetwork.PhoneIsClient("wlan0", 3, address("192.168.1.44"), 24)

private fun phone(
    encoders: Set<CodecId> = setOf(CodecId.H264, CodecId.H265),
    encoderProbe: ProbeOutcome = ProbeOutcome.SUPPORTED,
    virtualDisplayProbe: ProbeOutcome = ProbeOutcome.SUPPORTED,
    capture: Boolean = true,
) = PhoneCapabilities(
    apiLevel = 34,
    deviceName = "Pixel",
    screenWidth = 1080,
    screenHeight = 2400,
    densityDpi = 420,
    hardwareVideoEncoders = encoders,
    encoderProbe = encoderProbe,
    virtualDisplayProbe = virtualDisplayProbe,
    screenCaptureConsentAvailable = capture,
)

private fun readyDevice() = ReceiverDevice(
    address = "192.168.43.31",
    friendlyName = "Fire TV Stick",
    platform = ReceiverPlatform.FIRE_OS_8,
    adbState = AdbConnectionState.CONNECTED,
)

class CapabilityReportTest {
    private fun verdict(mode: CastMode) = ModeVerdict(mode, ModeStatus.AVAILABLE, "Fine.")

    @Test
    fun `a report must judge every mode exactly once`() {
        assertFailsWith<IllegalArgumentException> {
            CapabilityReport(hostNetwork, phone(), null, null, listOf(verdict(CastMode.MIRROR)))
        }
        assertFailsWith<IllegalArgumentException> {
            CapabilityReport(
                hostNetwork,
                phone(),
                null,
                null,
                listOf(verdict(CastMode.MIRROR), verdict(CastMode.MIRROR), verdict(CastMode.MIRROR)),
            )
        }
    }

    @Test
    fun `the indexer finds each mode and anythingOfferable reflects the set`() {
        val report = assess(hostNetwork, phone(), readyDevice())
        assertEquals(CastMode.SECOND_SCREEN, report[CastMode.SECOND_SCREEN].mode)
        assertTrue(report.anythingOfferable)

        val nothing = assess(LocalNetwork.NoLocalNetwork, phone(), null)
        assertFalse(nothing.anythingOfferable)
    }
}

class MobileCapabilityAssessorTest {
    @Test
    fun `every mode is judged, in the same order the desktop reports them`() {
        val report = assess(hostNetwork, phone(), readyDevice())
        assertEquals(
            listOf(CastMode.MIRROR, CastMode.SECOND_SCREEN, CastMode.MEDIA_HANDOFF),
            report.verdicts.map { it.mode },
        )
    }

    @Test
    fun `a blocking verdict carries its own mode rather than the placeholder it was built with`() {
        // The reachability helper returns a verdict carrying a placeholder mode; every caller has to
        // overwrite it, and forgetting to would label all three cards "Mirror this screen".
        val report = assess(LocalNetwork.NoLocalNetwork, phone(), null)
        assertEquals(
            listOf(CastMode.MIRROR, CastMode.SECOND_SCREEN, CastMode.MEDIA_HANDOFF),
            report.verdicts.map { it.mode },
        )
    }

    @Test
    fun `no local network blocks everything and says what to do about it`() {
        val report = assess(LocalNetwork.NoLocalNetwork, phone(), readyDevice())
        report.verdicts.forEach {
            assertEquals(ModeStatus.BLOCKED, it.status)
            assertNotNull(it.remedy)
            assertTrue(it.reason.contains("Mobile data"), it.reason)
        }
    }

    @Test
    fun `a missing television is explained differently depending on which end of the network this is`() {
        val asHost = assess(hostNetwork, phone(), null)[CastMode.MIRROR]
        val asClient = assess(clientNetwork, phone(), null)[CastMode.MIRROR]

        assertEquals(ModeStatus.BLOCKED, asHost.status)
        assertEquals(ModeStatus.BLOCKED, asClient.status)
        val hostRemedy = assertNotNull(asHost.remedy)
        val clientRemedy = assertNotNull(asClient.remedy)
        assertTrue(hostRemedy.contains("hotspot"), hostRemedy)
        assertTrue(clientRemedy.contains("clients from reaching each other"), clientRemedy)
        assertTrue(hostRemedy != clientRemedy)
    }

    @Test
    fun `a vega device makes every mode impossible and offers nothing at all`() {
        val vega = ReceiverDevice("192.168.43.31", platform = ReceiverPlatform.VEGA)
        val report = assess(hostNetwork, phone(), vega)
        report.verdicts.forEach {
            assertEquals(ModeStatus.IMPOSSIBLE, it.status)
            assertNull(it.remedy)
            assertTrue(it.reason.contains("no future version will change that"), it.reason)
        }
    }

    @Test
    fun `pairing evidence clears the adb gates but never clears vega`() {
        val unauthorised = ReceiverDevice(
            "192.168.43.31",
            platform = ReceiverPlatform.FIRE_OS_8,
            adbState = AdbConnectionState.UNAUTHORIZED,
        )
        assertEquals(
            ModeStatus.BLOCKED,
            assess(hostNetwork, phone(), unauthorised)[CastMode.MIRROR].status,
        )
        assertEquals(
            ModeStatus.AVAILABLE,
            assess(hostNetwork, phone(), unauthorised, pairedSessionActive = true)[CastMode.MIRROR]
                .status,
        )
        assertEquals(
            ModeStatus.AVAILABLE,
            assess(hostNetwork, phone(), unauthorised.copy(receiverAnswered = true))[CastMode.MIRROR]
                .status,
        )

        val vega = ReceiverDevice("192.168.43.31", platform = ReceiverPlatform.VEGA, receiverAnswered = true)
        assertEquals(
            ModeStatus.IMPOSSIBLE,
            assess(hostNetwork, phone(), vega, pairedSessionActive = true)[CastMode.MIRROR]
                .status,
        )
    }

    @Test
    fun `each adb state on an installable platform gets its own answer`() {
        fun statusFor(state: AdbConnectionState) = assess(
            hostNetwork,
            phone(),
            ReceiverDevice("192.168.43.31", platform = ReceiverPlatform.FIRE_OS_7, adbState = state),
        )[CastMode.MIRROR]

        assertEquals(ModeStatus.AVAILABLE, statusFor(AdbConnectionState.CONNECTED).status)

        val unauthorised = statusFor(AdbConnectionState.UNAUTHORIZED)
        assertEquals(ModeStatus.BLOCKED, unauthorised.status)
        val unauthorisedRemedy = assertNotNull(unauthorised.remedy)
        assertTrue(unauthorisedRemedy.contains("authorisation prompt"), unauthorisedRemedy)

        listOf(AdbConnectionState.NOT_PROBED, AdbConnectionState.TIMED_OUT, AdbConnectionState.REFUSED)
            .forEach {
                val verdict = statusFor(it)
                assertEquals(ModeStatus.BLOCKED, verdict.status)
                val remedy = assertNotNull(verdict.remedy)
                assertTrue(remedy.contains("awake"), remedy)
            }
    }

    @Test
    fun `a refusal and a silence are told apart, because only one of them is worth acting on`() {
        fun verdictFor(state: AdbConnectionState) = assess(
            hostNetwork,
            phone(),
            ReceiverDevice("192.168.43.77", platform = ReceiverPlatform.UNKNOWN, adbState = state),
        )[CastMode.MIRROR]

        val refused = verdictFor(AdbConnectionState.REFUSED)
        val silent = verdictFor(AdbConnectionState.TIMED_OUT)

        assertTrue(refused.reason.contains("192.168.43.77"), refused.reason)
        assertTrue(silent.reason.contains("192.168.43.77"), silent.reason)
        val refusedRemedy = assertNotNull(refused.remedy)
        val silentRemedy = assertNotNull(silent.remedy)
        assertTrue(refusedRemedy.contains("Developer Options"), refusedRemedy)
        assertFalse(silentRemedy.contains("Developer Options"), silentRemedy)
        assertTrue(refused.reason != silent.reason)
    }

    @Test
    fun `an unrun encoder probe blocks both streaming modes and leaves media handoff alone`() {
        val report = assess(
            hostNetwork,
            phone(encoders = emptySet(), encoderProbe = ProbeOutcome.NOT_PROBED),
            readyDevice(),
        )
        assertEquals(ModeStatus.BLOCKED, report[CastMode.MIRROR].status)
        assertEquals(ModeStatus.BLOCKED, report[CastMode.SECOND_SCREEN].status)
        assertEquals(ModeStatus.AVAILABLE, report[CastMode.MEDIA_HANDOFF].status)
        assertTrue(assertNotNull(report[CastMode.MIRROR].remedy).contains("encoder check"))
    }

    @Test
    fun `a phone with no hardware encoder can never stream, and is told so`() {
        listOf(
            phone(encoders = emptySet(), encoderProbe = ProbeOutcome.UNSUPPORTED),
            phone(encoders = emptySet(), encoderProbe = ProbeOutcome.NOT_PROBED).copy(
                encoderProbe = ProbeOutcome.UNSUPPORTED,
            ),
        ).forEach { capabilities ->
            val report = assess(hostNetwork, capabilities, readyDevice())
            assertEquals(ModeStatus.IMPOSSIBLE, report[CastMode.MIRROR].status)
            assertEquals(ModeStatus.IMPOSSIBLE, report[CastMode.SECOND_SCREEN].status)
            assertNull(report[CastMode.MIRROR].remedy)
        }
    }

    @Test
    fun `a phone that cannot capture its screen can still drive a second screen`() {
        // The divergence from Windows that this whole module exists to make expressible. There, a
        // second screen is a mirror plus a display driver, so it inherits every mirror blocker. Here
        // it renders into a display this app owns and needs no capture consent, so the two verdicts
        // are genuinely independent.
        val report = assess(hostNetwork, phone(capture = false), readyDevice())
        assertEquals(ModeStatus.IMPOSSIBLE, report[CastMode.MIRROR].status)
        assertEquals(ModeStatus.AVAILABLE, report[CastMode.SECOND_SCREEN].status)
        assertTrue(report[CastMode.MIRROR].reason.contains("judged separately"), report[CastMode.MIRROR].reason)
    }

    @Test
    fun `an unrun second-screen check blocks only the second screen`() {
        val report = assess(
            hostNetwork,
            phone(virtualDisplayProbe = ProbeOutcome.NOT_PROBED),
            readyDevice(),
        )
        assertEquals(ModeStatus.AVAILABLE, report[CastMode.MIRROR].status)
        assertEquals(ModeStatus.BLOCKED, report[CastMode.SECOND_SCREEN].status)
        val remedy = assertNotNull(report[CastMode.SECOND_SCREEN].remedy)
        assertTrue(remedy.contains("second-screen check"), remedy)
    }

    @Test
    fun `a phone that refuses the private display makes only the second screen impossible`() {
        val report = assess(
            hostNetwork,
            phone(virtualDisplayProbe = ProbeOutcome.UNSUPPORTED),
            readyDevice(),
        )
        assertEquals(ModeStatus.AVAILABLE, report[CastMode.MIRROR].status)
        assertEquals(ModeStatus.IMPOSSIBLE, report[CastMode.SECOND_SCREEN].status)
        assertNull(report[CastMode.SECOND_SCREEN].remedy)
    }

    @Test
    fun `a slow round trip blocks both streaming modes and quotes the measurement`() {
        val path = NetworkPath(roundTripMs = 55.0, jitterMs = 4.0, throughputMbps = 60.0, packetLossPercent = 0.0)
        val report = assess(hostNetwork, phone(), readyDevice(), path)
        listOf(CastMode.MIRROR, CastMode.SECOND_SCREEN).forEach {
            assertEquals(ModeStatus.BLOCKED, report[it].status)
            assertTrue(report[it].reason.contains("55 ms"), report[it].reason)
            assertTrue(report[it].reason.contains("30 ms"), report[it].reason)
        }
        assertEquals(ModeStatus.AVAILABLE, report[CastMode.MEDIA_HANDOFF].status)
    }

    @Test
    fun `a thin link blocks streaming, and a thinner one blocks handing a file over too`() {
        val thin = NetworkPath(5.0, 1.0, 12.0, 0.0)
        val thinner = NetworkPath(5.0, 1.0, 4.0, 0.0)

        val streaming = assess(hostNetwork, phone(), readyDevice(), thin)
        assertEquals(ModeStatus.BLOCKED, streaming[CastMode.MIRROR].status)
        assertTrue(streaming[CastMode.MIRROR].reason.contains("12.0 Mbit/s"))
        assertEquals(ModeStatus.AVAILABLE, streaming[CastMode.MEDIA_HANDOFF].status)

        val handoff = assess(hostNetwork, phone(), readyDevice(), thinner)
        assertEquals(ModeStatus.BLOCKED, handoff[CastMode.MEDIA_HANDOFF].status)
        assertTrue(handoff[CastMode.MEDIA_HANDOFF].reason.contains("8 Mbit/s"))
        assertNotNull(handoff[CastMode.MEDIA_HANDOFF].remedy)
    }

    @Test
    fun `an unmeasured capacity blocks nothing but is never passed off as proven`() {
        val path = NetworkPath(5.0, 1.0, 0.0, 0.0, throughputMeasured = false)
        val report = assess(hostNetwork, phone(), readyDevice(), path)
        report.verdicts.forEach {
            assertEquals(ModeStatus.AVAILABLE, it.status)
            assertTrue(it.reason.contains("capacity is still unproven"), it.reason)
        }
    }

    @Test
    fun `being a client of somebody else's network is said out loud, and being the host is not`() {
        val asClient = assess(clientNetwork, phone(), readyDevice())
        asClient.verdicts.forEach {
            assertTrue(it.reason.contains("isolates its clients"), it.reason)
        }

        val asHost = assess(hostNetwork, phone(), readyDevice())
        asHost.verdicts.forEach {
            assertFalse(it.reason.contains("isolates its clients"), it.reason)
        }
    }

    @Test
    fun `an offered second screen never leaves out what it cannot do`() {
        val report = assess(hostNetwork, phone(), readyDevice())
        val secondScreen = report[CastMode.SECOND_SCREEN]
        assertEquals(ModeStatus.AVAILABLE, secondScreen.status)
        assertTrue(secondScreen.reason.contains(MobileCapabilityAssessor.SECOND_SCREEN_BOUNDARY))
    }

    @Test
    fun `a mirror that is offered says that consent is asked for every time`() {
        val mirror = assess(hostNetwork, phone(), readyDevice())[CastMode.MIRROR]
        assertTrue(mirror.reason.contains("every time a mirror starts"), mirror.reason)
    }

    @Test
    fun `across every combination, a blocker is actionable and an impossibility is not`() {
        val networks = listOf(hostNetwork, clientNetwork, LocalNetwork.NoLocalNetwork)
        val devices = listOf<ReceiverDevice?>(null) + ReceiverPlatform.entries.flatMap { platform ->
            AdbConnectionState.entries.map { state ->
                ReceiverDevice("192.168.43.31", platform = platform, adbState = state)
            }
        }
        val phones = listOf(
            phone(),
            phone(capture = false),
            phone(encoderProbe = ProbeOutcome.NOT_PROBED, encoders = emptySet()),
            phone(encoderProbe = ProbeOutcome.UNSUPPORTED, encoders = emptySet()),
            phone(virtualDisplayProbe = ProbeOutcome.NOT_PROBED),
            phone(virtualDisplayProbe = ProbeOutcome.UNSUPPORTED),
        )
        val paths = listOf(
            null,
            NetworkPath(5.0, 1.0, 60.0, 0.0),
            NetworkPath(55.0, 9.0, 60.0, 0.0),
            NetworkPath(5.0, 1.0, 4.0, 0.0),
            NetworkPath(5.0, 1.0, 0.0, 0.0, throughputMeasured = false),
        )

        var checked = 0
        for (network in networks) {
            for (device in devices) {
                for (capabilities in phones) {
                    for (path in paths) {
                        val report = assess(network, capabilities, device, path)
                        report.verdicts.forEach { verdict ->
                            checked++
                            when (verdict.status) {
                                ModeStatus.BLOCKED -> assertNotNull(
                                    verdict.remedy,
                                    "A blocker the person cannot act on should have been impossible: $verdict",
                                )

                                ModeStatus.IMPOSSIBLE, ModeStatus.NOT_IMPLEMENTED -> assertNull(
                                    verdict.remedy,
                                    "Offering a remedy for an impossibility is a lie: $verdict",
                                )

                                ModeStatus.AVAILABLE -> assertNull(verdict.remedy)
                            }
                            assertTrue(verdict.reason.isNotBlank())
                        }
                    }
                }
            }
        }
        assertTrue(checked > 1_000, "the matrix should be wide enough to be worth running")
    }
}

/**
 * The assessor's own signature, kept here so these tests read as the evidence they supply.
 *
 * [MobileCapabilityAssessor.assess] takes one [AssessmentInput] rather than five loose arguments,
 * which is right for the callers that carry that evidence around and wrong for a test that wants
 * to name one piece of it and default the rest.
 */
private fun assess(
    network: LocalNetwork,
    phone: PhoneCapabilities,
    device: ReceiverDevice?,
    path: NetworkPath? = null,
    pairedSessionActive: Boolean = false,
): CapabilityReport = MobileCapabilityAssessor.assess(
    AssessmentInput(
        network = network,
        phone = phone,
        device = device,
        path = path,
        pairedSessionActive = pairedSessionActive,
    ),
)
