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
 * Every user-facing constant this module owns.
 *
 * Listed explicitly rather than discovered by reflection: a reflective sweep would silently stop
 * covering a new surface the day somebody adds one without noticing, and the point of the list is
 * that adding a surface makes somebody come here.
 */
private val everyCopyConstant: List<Pair<String, String>> = buildList {
    fun add(label: String, value: String) = add(label to value)

    add("CastCopy.empty.title", CastCopy.empty.title)
    add("CastCopy.empty.body", CastCopy.empty.body)
    add("CastCopy.SECTION_MODES", CastCopy.SECTION_MODES)
    add("CastCopy.SECTION_NETWORK", CastCopy.SECTION_NETWORK)
    add("CastCopy.SECTION_RECEIVERS", CastCopy.SECTION_RECEIVERS)
    add("CastCopy.PROBE_ACTION", CastCopy.PROBE_ACTION)
    add("CastCopy.PAIR_ACTION", CastCopy.PAIR_ACTION)
    add("CastCopy.MANUAL_ACTION", CastCopy.MANUAL_ACTION)

    add("PairingCopy.TITLE", PairingCopy.TITLE)
    add("PairingCopy.BODY", PairingCopy.BODY)
    add("PairingCopy.INVALID_CODE", PairingCopy.INVALID_CODE)
    add("PairingCopy.TIMED_OUT", PairingCopy.TIMED_OUT)

    add("ScreenCopy.empty.title", ScreenCopy.empty.title)
    add("ScreenCopy.empty.body", ScreenCopy.empty.body)
    add("ScreenCopy.MIRROR_CONSENT", ScreenCopy.MIRROR_CONSENT)
    add("ScreenCopy.SECOND_SCREEN_NO_CONSENT", ScreenCopy.SECOND_SCREEN_NO_CONSENT)
    add("ScreenCopy.CONGESTED_EXPLANATION", ScreenCopy.CONGESTED_EXPLANATION)

    add("MediaCopy.empty.title", MediaCopy.empty.title)
    add("MediaCopy.empty.body", MediaCopy.empty.body)
    add("MediaCopy.WHY_PUSHED", MediaCopy.WHY_PUSHED)

    add("SettingsCopy.SECOND_SCREEN_PROBE_EXPLANATION", SettingsCopy.SECOND_SCREEN_PROBE_EXPLANATION)
    add("SettingsCopy.PERMISSIONS_LINE", SettingsCopy.PERMISSIONS_LINE)

    add("DiagnosticsCopy.empty.title", DiagnosticsCopy.empty.title)
    add("DiagnosticsCopy.empty.body", DiagnosticsCopy.empty.body)
    add("DiagnosticsCopy.ROUND_TRIP_SOURCE", DiagnosticsCopy.ROUND_TRIP_SOURCE)

    add("NotificationCopy.CHANNEL_DESCRIPTION", NotificationCopy.CHANNEL_DESCRIPTION)

    add("ReceiverSetup.INSTALL_ACTION", ReceiverSetup.INSTALL_ACTION)
    add("ReceiverSetup.REMOVE_ACTION", ReceiverSetup.REMOVE_ACTION)

    add("MobileCapabilityAssessor.SECOND_SCREEN_BOUNDARY", MobileCapabilityAssessor.SECOND_SCREEN_BOUNDARY)

    OnboardingCopy.steps.forEachIndexed { index, step ->
        add("OnboardingCopy.steps[$index].title", step.title)
        add("OnboardingCopy.steps[$index].body", step.body)
        step.points.forEachIndexed { point, text ->
            add("OnboardingCopy.steps[$index].points[$point]", text)
        }
    }
}

class CopyVoiceTest {
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
        val phone = PhoneCapabilities(
            apiLevel = 34,
            deviceName = "Pixel",
            screenWidth = 1080,
            screenHeight = 2400,
            densityDpi = 420,
            hardwareVideoEncoders = setOf(CodecId.H264),
            encoderProbe = ProbeOutcome.SUPPORTED,
            virtualDisplayProbe = ProbeOutcome.SUPPORTED,
            screenCaptureConsentAvailable = true,
        )
        val devices = listOf<ReceiverDevice?>(null) + ReceiverPlatform.entries.map {
            ReceiverDevice("192.168.43.31", platform = it)
        }

        devices.forEach { device ->
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
