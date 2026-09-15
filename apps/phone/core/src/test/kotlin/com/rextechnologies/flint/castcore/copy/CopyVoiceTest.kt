package com.rextechnologies.flint.castcore.copy

import com.rextechnologies.flint.castcore.capability.AssessmentInput
import com.rextechnologies.flint.castcore.capability.CastMode
import com.rextechnologies.flint.castcore.capability.LocalNetwork
import com.rextechnologies.flint.castcore.capability.MobileCapabilityAssessor
import com.rextechnologies.flint.castcore.capability.ModeStatus
import com.rextechnologies.flint.castcore.capability.ModeVerdict
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.capability.ReceiverDevice
import com.rextechnologies.flint.castcore.capability.ReceiverPlatform
import com.rextechnologies.flint.castcore.capability.ToneIntent
import com.rextechnologies.flint.castcore.screen.SceneCopy
import com.rextechnologies.flint.castcore.setup.ReceiverSetup
import com.rextechnologies.flint.protocol.media.LinkHealth
import com.rextechnologies.flint.protocol.network.SelectedHotspotInterface
import com.rextechnologies.flint.protocol.wire.CodecId
import java.net.Inet4Address
import java.net.InetAddress
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

private fun address(value: String): Inet4Address = InetAddress.getByName(value) as Inet4Address

/**
 * Every user-facing constant this module owns, discovered rather than listed.
 *
 * The earlier version was a hand-maintained list, on the theory that adding a surface should make
 * somebody come here. It did not: three surfaces and a dozen constants were added without anybody
 * coming here, and the list silently covered less each time. Reflection over every `object` in the
 * copy package cannot fall behind, and the count assertion below is what keeps a sweep that matched
 * nothing from passing.
 *
 * Functions that take arguments cannot be enumerated and are sampled explicitly at the end.
 */
private val everyCopyConstant: List<Pair<String, String>> = buildList {
    fun add(label: String, value: String) = add(label to value)

    val surfaces = listOf(
        AudioCopy,
        CastCopy,
        PairingCopy,
        ScreenCopy,
        MediaCopy,
        SettingsCopy,
        DiagnosticsCopy,
        NotificationCopy,
        Placeholders,
        FailureCopy,
        ReceiverSetup,
        SceneCopy,
        MobileCapabilityAssessor,
    )
    surfaces.forEach { surface ->
        val name = surface::class.simpleName
        surface::class.java.declaredFields
            .filter { !it.name.equals("INSTANCE") }
            .forEach { field ->
                field.isAccessible = true
                when (val value = field.get(surface)) {
                    is String -> add("$name.${field.name}", value)
                    is EmptyStateCopy -> {
                        add("$name.${field.name}.title", value.title)
                        add("$name.${field.name}.body", value.body)
                    }
                }
            }
    }

    OnboardingCopy.steps.forEachIndexed { index, step ->
        add("OnboardingCopy.steps[$index].title", step.title)
        add("OnboardingCopy.steps[$index].body", step.body)
        step.points.forEachIndexed { point, text ->
            add("OnboardingCopy.steps[$index].points[$point]", text)
        }
    }

    // Parameterised copy, sampled at representative inputs.
    add("PairingCopy.paired", PairingCopy.paired("Fire TV Stick"))
    add("ScreenCopy.cockpitLine", ScreenCopy.cockpitLine("second screen", "Fire TV Stick"))
    add("ScreenCopy.encoderFailure", ScreenCopy.encoderFailure(""))
    add("ScreenCopy.encoderFailure(detail)", ScreenCopy.encoderFailure("no surface"))
    add("ScreenCopy.keyFrameFallback", ScreenCopy.keyFrameFallback(2))
    add("AudioCopy.failedSentence(detail)", AudioCopy.failedSentence("no AAC encoder"))
    add("AudioCopy.failedSentence(none)", AudioCopy.failedSentence(""))
    add("CastCopy.probeFailedBody(none)", CastCopy.probeFailedBody(emptyList()))
    add(
        "CastCopy.probeFailedBody(all)",
        CastCopy.probeFailedBody(com.rextechnologies.flint.castcore.discovery.DiscoveryRung.entries),
    )
    add(
        "SettingsCopy.encoderCheckResult(none)",
        SettingsCopy.encoderCheckResult(emptyList(), null, ProbeOutcome.NOT_PROBED, ""),
    )
    add(
        "SettingsCopy.encoderCheckResult(passed)",
        SettingsCopy.encoderCheckResult(listOf("H.264", "H.265"), "H.264", ProbeOutcome.SUPPORTED, "Intact."),
    )
    add(
        "SettingsCopy.encoderCheckResult(failed)",
        SettingsCopy.encoderCheckResult(listOf("H.264"), "H.264", ProbeOutcome.UNSUPPORTED, "The frame was green"),
    )
    add(
        "SettingsCopy.encoderCheckResult(unchecked)",
        SettingsCopy.encoderCheckResult(listOf("H.264"), null, ProbeOutcome.NOT_PROBED, ""),
    )
    add("MediaCopy.pushing", MediaCopy.pushing(3, 10))
    add("MediaCopy.sendingBytes", MediaCopy.sendingBytes(3L * 1024 * 1024))
    add("MediaCopy.sendingFraction", MediaCopy.sendingFraction(0.42))
    add("MediaCopy.sizeLabel", MediaCopy.sizeLabel(3L * 1024 * 1024).orEmpty())
    add("FailureCopy.unexpectedFailure", FailureCopy.unexpectedFailure("IllegalStateException"))
    add("NotificationCopy.text", NotificationCopy.text("Fire TV Stick"))
    add(
        "ReceiverSetup.removeConfirmation",
        ReceiverSetup.removeConfirmation("com.rextechnologies.flint.receiver.debug", "Fire TV Stick"),
    )
    add("ReceiverSetup.identified(installed)", ReceiverSetup.identified("Fire TV Stick", "Fire OS 8", true))
    add("ReceiverSetup.identified(missing)", ReceiverSetup.identified("Fire TV Stick", "Fire OS 8", false))
    add("ReceiverSetup.unauthorised", ReceiverSetup.unauthorised("Fire TV Stick"))
    add("ReceiverSetup.notAndroid", ReceiverSetup.notAndroid("Fire TV Stick"))
    add("ReceiverSetup.tooOld", ReceiverSetup.tooOld("Fire TV Stick", "Fire OS 5"))
    add("ReceiverSetup.installed", ReceiverSetup.installed("Fire TV Stick"))
    add("ReceiverSetup.removed", ReceiverSetup.removed("Fire TV Stick"))
    add("ReceiverSetup.failed(none)", ReceiverSetup.failed(""))
    add("ReceiverSetup.failed(detail)", ReceiverSetup.failed("Failure [INSTALL_FAILED_OLDER_SDK]"))
}

class CopyVoiceTest {
    @Test
    fun `the sweep is looking at the copy, not at an empty list`() {
        // Below the number that existed when the sweep became reflective. A refactor that moved the
        // copy out of this package would otherwise pass every test here by testing nothing.
        assertTrue(everyCopyConstant.size >= 90, "only ${everyCopyConstant.size} constants were found")
        assertEquals(everyCopyConstant.size, everyCopyConstant.map { it.first }.distinct().size)
    }

    @Test
    fun `no string in this app calls the cast link private, secure or encrypted`() {
        // The link is authorised, not encrypted. A token proves which phone is talking; it does not
        // hide what is said, and a reader who believes otherwise behaves differently on a shared
        // network.
        everyCopyConstant.forEach { (label, value) ->
            val claims = HonestyRules.confidentialityClaims(value)
            assertTrue(claims.isEmpty(), "$label makes a confidentiality claim: $claims")
        }
    }

    @Test
    fun `nothing in this app raises its voice`() {
        everyCopyConstant.forEach { (label, value) ->
            assertFalse(HonestyRules.raisesItsVoice(value), "$label has an exclamation mark")
        }
    }

    @Test
    fun `no piece of copy outgrows the card it has to fit in`() {
        everyCopyConstant.forEach { (label, value) ->
            assertTrue(
                value.length <= HonestyRules.MAXIMUM_COPY_LENGTH,
                "$label is ${value.length} characters",
            )
        }
    }

    @Test
    fun `every verdict the assessor can produce reads as a sentence and stays inside the bound`() {
        val host = LocalNetwork.PhoneIsHost(SelectedHotspotInterface("ap0", 7, address("192.168.43.1"), 24))
        // Every probe outcome for both probes, so that no sentence is reachable by the app and
        // unreachable by this test. Two sentences hid behind a fully probed phone for exactly that
        // reason.
        val phones = ProbeOutcome.entries.flatMap { encoder ->
            ProbeOutcome.entries.flatMap { display ->
                ProbeOutcome.entries.map { roundTrip ->
                    PhoneCapabilities(
                        apiLevel = 34,
                        deviceName = "Pixel",
                        screenWidth = 1080,
                        screenHeight = 2400,
                        densityDpi = 420,
                        hardwareVideoEncoders =
                        if (encoder == ProbeOutcome.SUPPORTED) setOf(CodecId.H264) else emptySet(),
                        encoderProbe = encoder,
                        virtualDisplayProbe = display,
                        // A frame cannot have survived an encoder that was not found.
                        encoderRoundTrip =
                        if (encoder == ProbeOutcome.SUPPORTED) roundTrip else ProbeOutcome.NOT_PROBED,
                        screenCaptureConsentAvailable = true,
                    )
                }
            }
        }
        val devices = listOf<ReceiverDevice?>(null) + ReceiverPlatform.entries.map {
            ReceiverDevice("192.168.43.31", platform = it, receiverAnswered = true)
        }

        phones.forEach { phone ->
            devices.forEach { device ->
                everyVerdict(host, phone, device)
            }
        }
    }

    private fun everyVerdict(host: LocalNetwork, phone: PhoneCapabilities, device: ReceiverDevice?) {
        run {
            MobileCapabilityAssessor.assess(AssessmentInput(host, phone, device)).verdicts.forEach { verdict ->
                assertTrue(HonestyRules.isCompleteSentence(verdict.reason), verdict.reason)
                assertFalse(HonestyRules.raisesItsVoice(verdict.reason), verdict.reason)
                assertTrue(HonestyRules.confidentialityClaims(verdict.reason).isEmpty(), verdict.reason)
                assertTrue(
                    verdict.reason.length <= HonestyRules.MAXIMUM_COPY_LENGTH,
                    "${verdict.mode} reason is ${verdict.reason.length} characters",
                )
                verdict.remedy?.let {
                    assertTrue(HonestyRules.isCompleteSentence(it), it)
                    assertTrue(it.length <= HonestyRules.MAXIMUM_COPY_LENGTH, it)
                }
            }
        }
    }
}

class OnboardingCopyTest {
    @Test
    fun `the introduction is six steps`() {
        assertEquals(6, OnboardingCopy.steps.size)
    }

    @Test
    fun `the limitation that decides everything is stated before anything is asked for`() {
        // Finding out on step five that the device in the room can never work is a worse experience
        // than being told on step two.
        val readThisFirst = OnboardingCopy.steps[1]
        assertEquals("Read this first", readThisFirst.eyebrow)
        assertEquals(ToneIntent.LIVE, readThisFirst.tone)
        assertTrue(readThisFirst.body.contains("Vega OS"), readThisFirst.body)
        assertTrue(OnboardingCopy.steps.none { it.ordered && OnboardingCopy.steps.indexOf(it) < 1 })
    }

    @Test
    fun `the eyebrows read in the order they are shown`() {
        assertEquals(
            listOf(
                "Welcome",
                "Read this first",
                "On the TV, in this order",
                "If nothing is found",
                "What the TV shows",
                "What works today",
            ),
            OnboardingCopy.steps.map { it.eyebrow },
        )
    }

    @Test
    fun `an ordered step numbers its points and an unordered one does not`() {
        val ordered = OnboardingCopy.steps.first { it.ordered }
        assertEquals(
            (1..ordered.points.size).toList(),
            ordered.displayPoints.map { it.number },
        )
        assertEquals(listOf("1", "2", "3", "4"), ordered.displayPoints.map { it.marker })

        val unordered = OnboardingCopy.steps.first { !it.ordered && it.points.isNotEmpty() }
        assertTrue(unordered.displayPoints.all { it.number == null })
        assertTrue(unordered.displayPoints.all { it.marker == "—" })
    }

    @Test
    fun `the introduction ends on an answer rather than an empty screen`() {
        assertEquals("Find my TV", OnboardingCopy.FINAL_ACTION)
        assertEquals("Skip", OnboardingCopy.SKIP_ACTION)
    }

    @Test
    fun `a step or a point with nothing in it is refused`() {
        assertFailsWith<IllegalArgumentException> { OnboardingStep(" ", "Title", "Body.") }
        assertFailsWith<IllegalArgumentException> { OnboardingStep("Eyebrow", " ", "Body.") }
        assertFailsWith<IllegalArgumentException> { OnboardingStep("Eyebrow", "Title", " ") }
        assertFailsWith<IllegalArgumentException> { OnboardingPoint(" ") }
        assertFailsWith<IllegalArgumentException> { OnboardingPoint("Text", number = 0) }
    }

    @Test
    fun `every body reads as a sentence`() {
        OnboardingCopy.steps.forEach {
            assertTrue(HonestyRules.isCompleteSentence(it.body), it.body)
            it.points.forEach { point -> assertTrue(HonestyRules.isCompleteSentence(point), point) }
        }
    }
}

class SurfacesCopyTest {
    private val host = LocalNetwork.PhoneIsHost(SelectedHotspotInterface("ap0", 7, address("192.168.43.1"), 24))
    private val client = LocalNetwork.PhoneIsClient("wlan0", 3, address("192.168.1.44"), 24)

    private fun report(offerable: Boolean, device: ReceiverDevice?) = com.rextechnologies.flint.castcore
        .capability.CapabilityReport(
            network = host,
            phone = PhoneCapabilities(34, "Pixel", 1080, 2400, 420),
            device = device,
            path = null,
            verdicts = CastMode.entries.map {
                ModeVerdict(
                    it,
                    if (offerable) ModeStatus.AVAILABLE else ModeStatus.IMPOSSIBLE,
                    "A reason.",
                )
            },
        )

    @Test
    fun `an empty state needs a glyph, a title and a body`() {
        assertFailsWith<IllegalArgumentException> { EmptyStateCopy(" ", "Title", "Body.") }
        assertFailsWith<IllegalArgumentException> { EmptyStateCopy("C", " ", "Body.") }
        assertFailsWith<IllegalArgumentException> { EmptyStateCopy("C", "Title", " ") }
    }

    @Test
    fun `the four tabs carry the desktop's own letterforms`() {
        assertEquals(listOf("C", "S", "M", "R"), MobileTab.entries.map { it.glyph })
        assertEquals(listOf("Cast", "Screen", "Media", "Settings"), MobileTab.entries.map { it.title })
        MobileTab.entries.forEach { assertEquals(it.eyebrow, it.eyebrow.uppercase()) }
    }

    @Test
    fun `the heading says exactly what stage the page is at`() {
        assertEquals("Probing", CastCopy.headingStatus(isProbing = true, report = null))
        assertEquals("Not probed", CastCopy.headingStatus(isProbing = false, report = null))
        assertEquals(
            "Connect TV",
            CastCopy.headingStatus(isProbing = false, report = report(offerable = true, device = null)),
        )
        assertEquals(
            "Ready",
            CastCopy.headingStatus(
                isProbing = false,
                report = report(offerable = true, device = ReceiverDevice("10.0.0.2")),
            ),
        )
        assertEquals(
            "Limited",
            CastCopy.headingStatus(
                isProbing = false,
                report = report(offerable = false, device = ReceiverDevice("10.0.0.2")),
            ),
        )
    }

    @Test
    fun `only a page with something offerable earns the accent`() {
        assertEquals(ToneIntent.NEUTRAL, CastCopy.headingTone(null))
        assertEquals(
            ToneIntent.SIGNAL,
            CastCopy.headingTone(report(offerable = true, device = ReceiverDevice("10.0.0.2"))),
        )
        assertEquals(
            ToneIntent.NEUTRAL,
            CastCopy.headingTone(report(offerable = false, device = ReceiverDevice("10.0.0.2"))),
        )
    }

    @Test
    fun `being the access point is said out loud as the good case`() {
        assertTrue(CastCopy.networkLine(host).contains("access point"))
        assertEquals(ToneIntent.SIGNAL, CastCopy.networkTone(host))

        assertTrue(CastCopy.networkLine(client).contains("somebody else runs"))
        assertEquals(ToneIntent.NEUTRAL, CastCopy.networkTone(client))

        assertTrue(CastCopy.networkLine(LocalNetwork.NoLocalNetwork).contains("Mobile data"))
        assertEquals(ToneIntent.LIVE, CastCopy.networkTone(LocalNetwork.NoLocalNetwork))
    }

    @Test
    fun `a pairing code is six digits and nothing else`() {
        assertTrue(PairingCopy.isWellFormed("004283"))
        assertFalse(PairingCopy.isWellFormed("00428"))
        assertFalse(PairingCopy.isWellFormed("0042833"))
        assertFalse(PairingCopy.isWellFormed("00428a"))
        assertFalse(PairingCopy.isWellFormed(""))
        assertEquals(6, PairingCopy.DIGITS)
    }

    @Test
    fun `a congested link is a word and a tone, not a number nobody measured`() {
        assertEquals("Headroom", ScreenCopy.linkHealthWord(LinkHealth.Headroom))
        assertEquals("Stable", ScreenCopy.linkHealthWord(LinkHealth.Stable))
        assertEquals("Congested", ScreenCopy.linkHealthWord(LinkHealth.Congested))
        assertEquals(ToneIntent.SIGNAL, ScreenCopy.linkHealthTone(LinkHealth.Headroom))
        assertEquals(ToneIntent.SIGNAL, ScreenCopy.linkHealthTone(LinkHealth.Stable))
        assertEquals(ToneIntent.LIVE, ScreenCopy.linkHealthTone(LinkHealth.Congested))
    }

    @Test
    fun `the cockpit line names both ends`() {
        val line = ScreenCopy.cockpitLine("a photo", "Fire TV Stick")
        assertTrue(line.contains("a photo"))
        assertTrue(line.contains("Fire TV Stick"))
    }

    @Test
    fun `progress is a percentage of what is actually being sent`() {
        assertEquals("Sending to the TV — 0%.", MediaCopy.pushing(0, 10))
        assertEquals("Sending to the TV — 50%.", MediaCopy.pushing(5, 10))
        assertEquals("Sending to the TV — 100%.", MediaCopy.pushing(10, 10))
        assertEquals("Sending to the TV — 100%.", MediaCopy.pushing(99, 10))
        assertFailsWith<IllegalArgumentException> { MediaCopy.pushing(0, 0) }
    }

    @Test
    fun `the notification says the mode and the television`() {
        assertEquals("Second screen", NotificationCopy.title("Second screen"))
        assertEquals("Sending to Fire TV Stick", NotificationCopy.text("Fire TV Stick"))
        assertEquals("flint-mobile-session", NotificationCopy.CHANNEL_ID)
    }

    @Test
    fun `nothing is ever shown as a number that was never measured`() {
        assertEquals("Not measured", Placeholders.NOT_MEASURED)
        assertEquals("Not probed", Placeholders.NOT_PROBED)
        assertEquals("Not reported", Placeholders.NOT_REPORTED)
        assertEquals("—", Placeholders.NONE)
        assertEquals("No receiver found", Placeholders.NO_RECEIVER)
    }
}
