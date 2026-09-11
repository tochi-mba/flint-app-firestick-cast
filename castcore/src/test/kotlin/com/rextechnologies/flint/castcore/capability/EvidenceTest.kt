package com.rextechnologies.flint.castcore.capability

import com.rextechnologies.flint.protocol.wire.CodecId
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class ReceiverPlatformTest {
    @Test
    fun `every platform has a display label that reads as a product name`() {
        assertEquals("Unknown", ReceiverPlatform.UNKNOWN.displayLabel)
        assertEquals("Fire OS 5", ReceiverPlatform.FIRE_OS_5.displayLabel)
        assertEquals("Fire OS 6", ReceiverPlatform.FIRE_OS_6.displayLabel)
        assertEquals("Fire OS 7", ReceiverPlatform.FIRE_OS_7.displayLabel)
        assertEquals("Fire OS 8", ReceiverPlatform.FIRE_OS_8.displayLabel)
        assertEquals("Fire OS 14", ReceiverPlatform.FIRE_OS_14.displayLabel)
        assertEquals("Fire OS 16", ReceiverPlatform.FIRE_OS_16.displayLabel)
        assertEquals("Vega OS", ReceiverPlatform.VEGA.displayLabel)
    }

    @Test
    fun `every fire os generation is android underneath`() {
        val fireOs = listOf(
            ReceiverPlatform.FIRE_OS_5,
            ReceiverPlatform.FIRE_OS_6,
            ReceiverPlatform.FIRE_OS_7,
            ReceiverPlatform.FIRE_OS_8,
            ReceiverPlatform.FIRE_OS_14,
            ReceiverPlatform.FIRE_OS_16,
        )
        fireOs.forEach { assertTrue(it.isAndroidBased(), "${it.displayLabel} is Android") }
        fireOs.forEach { assertTrue(it.canInstallReceiver()) }
    }

    @Test
    fun `an undetermined platform is not a capability`() {
        // Reporting UNKNOWN as installable would be a guess dressed up as an answer.
        assertFalse(ReceiverPlatform.UNKNOWN.isAndroidBased())
        assertFalse(ReceiverPlatform.UNKNOWN.canInstallReceiver())
    }

    @Test
    fun `vega can never install a receiver`() {
        assertFalse(ReceiverPlatform.VEGA.isAndroidBased())
        assertFalse(ReceiverPlatform.VEGA.canInstallReceiver())
    }

    @Test
    fun `the enums a diagnostics row switches on are all present`() {
        assertEquals(3, ProbeOutcome.entries.size)
        assertEquals(5, AdbConnectionState.entries.size)
        assertEquals(5, DiscoverySource.entries.size)
        assertEquals(8, ReceiverPlatform.entries.size)
    }
}

class ReceiverDeviceTest {
    @Test
    fun `the receiver port is the one the receiver actually listens on`() {
        assertEquals(47_855, ReceiverDevice.DEFAULT_RECEIVER_PORT)
    }

    @Test
    fun `a device without an address is not a device`() {
        assertFailsWith<IllegalArgumentException> { ReceiverDevice(address = " ") }
    }

    @Test
    fun `an impossible port is refused`() {
        assertFailsWith<IllegalArgumentException> { ReceiverDevice("10.0.0.2", port = 0) }
        assertFailsWith<IllegalArgumentException> { ReceiverDevice("10.0.0.2", port = 65_536) }
    }

    @Test
    fun `a negative api level is refused`() {
        assertFailsWith<IllegalArgumentException> { ReceiverDevice("10.0.0.2", androidApiLevel = -1) }
    }

    @Test
    fun `an unauthorised device still counts as reachable`() {
        // Something is there and it answered; it simply has not said yes yet.
        assertTrue(ReceiverDevice("10.0.0.2", adbState = AdbConnectionState.UNAUTHORIZED).isReachable)
        assertTrue(ReceiverDevice("10.0.0.2", adbState = AdbConnectionState.CONNECTED).isReachable)
        assertFalse(ReceiverDevice("10.0.0.2", adbState = AdbConnectionState.REFUSED).isReachable)
        assertFalse(ReceiverDevice("10.0.0.2", adbState = AdbConnectionState.TIMED_OUT).isReachable)
        assertFalse(ReceiverDevice("10.0.0.2", adbState = AdbConnectionState.NOT_PROBED).isReachable)
    }

    @Test
    fun `display name falls back from the friendly name to the model to the address`() {
        assertEquals(
            "Living room",
            ReceiverDevice("10.0.0.2", friendlyName = "Living room", model = "AFTKA").displayName,
        )
        assertEquals("AFTKA", ReceiverDevice("10.0.0.2", model = "AFTKA").displayName)
        assertEquals("10.0.0.2", ReceiverDevice("10.0.0.2").displayName)
    }
}

class PhoneCapabilitiesTest {
    private fun phone(
        encoders: Set<CodecId> = setOf(CodecId.H264),
        probe: ProbeOutcome = ProbeOutcome.SUPPORTED,
    ) = PhoneCapabilities(
        apiLevel = 34,
        deviceName = "Pixel",
        screenWidth = 1080,
        screenHeight = 2400,
        densityDpi = 420,
        hardwareVideoEncoders = encoders,
        encoderProbe = probe,
    )

    @Test
    fun `a probe that found no encoder has not found support`() {
        assertFailsWith<IllegalArgumentException> {
            phone(encoders = emptySet(), probe = ProbeOutcome.SUPPORTED)
        }
    }

    @Test
    fun `an unrun probe with no encoders is perfectly legitimate`() {
        val unprobed = phone(encoders = emptySet(), probe = ProbeOutcome.NOT_PROBED)
        assertEquals(ProbeOutcome.NOT_PROBED, unprobed.encoderProbe)
        assertTrue(unprobed.advertisedCodecs.isEmpty())
    }

    @Test
    fun `the advertised codecs are the ones the hardware reported`() {
        assertEquals(setOf(CodecId.H264, CodecId.H265), phone(setOf(CodecId.H264, CodecId.H265)).advertisedCodecs)
    }

    @Test
    fun `nonsense about the device is refused rather than carried`() {
        assertFailsWith<IllegalArgumentException> { phone().copy(apiLevel = 0) }
        assertFailsWith<IllegalArgumentException> { phone().copy(deviceName = " ") }
        assertFailsWith<IllegalArgumentException> { phone().copy(screenWidth = 0) }
        assertFailsWith<IllegalArgumentException> { phone().copy(screenHeight = 0) }
        assertFailsWith<IllegalArgumentException> { phone().copy(densityDpi = 0) }
    }

    @Test
    fun `the defaults claim nothing`() {
        val bare = PhoneCapabilities(30, "Pixel", 1080, 2400, 420)
        assertEquals(ProbeOutcome.NOT_PROBED, bare.encoderProbe)
        assertEquals(ProbeOutcome.NOT_PROBED, bare.virtualDisplayProbe)
        assertFalse(bare.screenCaptureConsentAvailable)
        assertFalse(bare.audioPlaybackCaptureSupported)
    }
}

class NetworkPathTest {
    @Test
    fun `the thresholds are the same numbers the windows host uses`() {
        assertEquals(20.0, NetworkPath.MINIMUM_MIRROR_THROUGHPUT_MBPS)
        assertEquals(30.0, NetworkPath.USABLE_ROUND_TRIP_CEILING_MS)
        assertEquals(8.0, NetworkPath.MINIMUM_MEDIA_HANDOFF_MBPS)
    }

    @Test
    fun `a round trip at the ceiling is still usable`() {
        assertTrue(NetworkPath(30.0, 1.0, 40.0, 0.0).latencySupportsMirroring)
        assertFalse(NetworkPath(30.1, 1.0, 40.0, 0.0).latencySupportsMirroring)
    }

    @Test
    fun `an unknown capacity is not a failing one`() {
        val unmeasured = NetworkPath(5.0, 1.0, 0.0, 0.0, throughputMeasured = false)
        assertFalse(unmeasured.throughputTooLowForMirroring)
        assertEquals("Not measured", unmeasured.throughputLabel)
    }

    @Test
    fun `a measured shortfall is reported as one`() {
        val slow = NetworkPath(5.0, 1.0, 12.0, 0.0)
        assertTrue(slow.throughputTooLowForMirroring)
        assertEquals("12.0 Mbit/s", slow.throughputLabel)
    }

    @Test
    fun `exactly the minimum is enough`() {
        assertFalse(NetworkPath(5.0, 1.0, 20.0, 0.0).throughputTooLowForMirroring)
    }

    @Test
    fun `numbers are formatted without a locale so two sentences cannot disagree`() {
        assertEquals("0.0", NetworkPath.oneDecimal(0.0))
        assertEquals("8.0", NetworkPath.oneDecimal(8.0))
        assertEquals("12.3", NetworkPath.oneDecimal(12.34))
        assertEquals("12.4", NetworkPath.oneDecimal(12.36))
        assertEquals("8.5 Mbit/s", NetworkPath.formatMbps(8.46))
        assertEquals("30", NetworkPath.wholeNumber(30.0))
        assertEquals("31", NetworkPath.wholeNumber(30.6))
    }

    @Test
    fun `a measurement that cannot have happened is refused`() {
        assertFailsWith<IllegalArgumentException> { NetworkPath(-1.0, 0.0, 0.0, 0.0) }
        assertFailsWith<IllegalArgumentException> { NetworkPath(1.0, -1.0, 0.0, 0.0) }
        assertFailsWith<IllegalArgumentException> { NetworkPath(1.0, 0.0, -1.0, 0.0) }
        assertFailsWith<IllegalArgumentException> { NetworkPath(1.0, 0.0, 0.0, 101.0) }
        assertFailsWith<IllegalArgumentException> { NetworkPath(Double.NaN, 0.0, 0.0, 0.0) }
        assertFailsWith<IllegalArgumentException> { NetworkPath(1.0, Double.POSITIVE_INFINITY, 0.0, 0.0) }
        assertFailsWith<IllegalArgumentException> { NetworkPath(1.0, 0.0, Double.NaN, 0.0) }
    }
}
