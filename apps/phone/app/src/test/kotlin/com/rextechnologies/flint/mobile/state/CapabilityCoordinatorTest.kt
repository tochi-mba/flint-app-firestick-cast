package com.rextechnologies.flint.mobile.state

import android.app.Activity
import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.mobile.platform.CapabilityStore
import com.rextechnologies.flint.mobile.platform.EncoderRoundTrip
import com.rextechnologies.flint.mobile.platform.StoredCapabilities
import com.rextechnologies.flint.protocol.wire.CodecId
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.async
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.yield
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertTrue
import kotlin.test.fail

/** The coordinator's own decisions about the checks, with the platform calls faked. */
@RunWith(AndroidJUnit4::class)
class CapabilityCoordinatorTest {
    private val context: Context = ApplicationProvider.getApplicationContext()
    private val activity: Activity = Robolectric.buildActivity(Activity::class.java).setup().get()

    private fun coordinator(
        encoders: Set<CodecId> = setOf(CodecId.H264, CodecId.H265),
        roundTrip: suspend (Activity, CodecId) -> EncoderRoundTrip.Outcome = { _, _ ->
            EncoderRoundTrip.Outcome(ProbeOutcome.SUPPORTED, "Intact.")
        },
        display: ProbeOutcome = ProbeOutcome.SUPPORTED,
        store: CapabilityStore = RecordingStore(),
    ) = CapabilityCoordinator(
        context = context,
        deviceName = "Pixel",
        screenWidth = 1080,
        screenHeight = 2400,
        densityDpi = 420,
        enumerateEncoders = { encoders },
        roundTrip = roundTrip,
        probeDisplay = { display },
        store = store,
    )

    /** A store in memory, so these tests never touch a preference file. */
    private class RecordingStore(var value: StoredCapabilities? = null) : CapabilityStore {
        var saves = 0
            private set

        override fun load(): StoredCapabilities? = value

        override fun save(value: StoredCapabilities) {
            this.value = value
            saves++
        }
    }

    @Test
    fun `the encoder check lists the encoders, exercises the preferred one, and remembers both`() = runBlocking {
        var exercised: CodecId? = null
        val coordinator = coordinator(roundTrip = { _, codec ->
            exercised = codec
            EncoderRoundTrip.Outcome(ProbeOutcome.SUPPORTED, "Intact.")
        })

        val check = coordinator.probeEncoders(activity)

        assertEquals(CodecId.H264, exercised)
        assertEquals(setOf(CodecId.H264, CodecId.H265), check.encoders)
        assertEquals(ProbeOutcome.SUPPORTED, check.roundTrip)
        val state = coordinator.state.value
        assertEquals(ProbeOutcome.SUPPORTED, state.encoderProbe)
        assertEquals(ProbeOutcome.SUPPORTED, state.encoderRoundTrip)
        assertEquals("Intact.", state.roundTripDetail)
        assertEquals(CodecId.H264, state.roundTripCodec)
        assertFalse(state.encoderProbeRunning)

        val capabilities = coordinator.capabilities()
        assertEquals(ProbeOutcome.SUPPORTED, capabilities.encoderRoundTrip)
        assertEquals("Intact.", capabilities.roundTripDetail)
    }

    @Test
    fun `with no hardware encoder the round trip is never attempted`() = runBlocking {
        val coordinator = coordinator(encoders = emptySet(), roundTrip = { _, _ -> fail("nothing to exercise") })
        val check = coordinator.probeEncoders(activity)
        assertEquals(ProbeOutcome.UNSUPPORTED, coordinator.state.value.encoderProbe)
        assertEquals(ProbeOutcome.NOT_PROBED, check.roundTrip)
        assertEquals(null, check.through)
    }

    @Test
    fun `a phone that refused a private display gets an unchecked encoder rather than a failed one`() = runBlocking {
        // The round trip draws into the same display the second screen would. A failure to draw is
        // not evidence against the encoder, so it is reported as not run, and says why.
        val coordinator = coordinator(
            roundTrip = { _, _ -> fail("the round trip must not run without a display") },
            display = ProbeOutcome.UNSUPPORTED,
        )
        assertEquals(ProbeOutcome.UNSUPPORTED, coordinator.probeSecondScreen(activity))

        val check = coordinator.probeEncoders(activity)
        assertEquals(ProbeOutcome.NOT_PROBED, check.roundTrip)
        assertEquals(CapabilityCoordinator.DISPLAY_REFUSED_DETAIL, check.detail)
        assertEquals(ProbeOutcome.SUPPORTED, coordinator.state.value.encoderProbe)
    }

    @Test
    fun `a frame that decodes to nothing is a failed encoder, with the decoder's own detail kept`() = runBlocking {
        val coordinator = coordinator(roundTrip = { _, _ ->
            EncoderRoundTrip.Outcome(ProbeOutcome.UNSUPPORTED, "The top left quadrant came back as rgb(0, 200, 0).")
        })
        coordinator.probeEncoders(activity)
        val capabilities = coordinator.capabilities()
        assertEquals(ProbeOutcome.UNSUPPORTED, capabilities.encoderRoundTrip)
        assertTrue(capabilities.roundTripDetail.contains("top left"), capabilities.roundTripDetail)
    }

    @Test
    fun `a second press while a check is running is answered from the first rather than run twice`() = runBlocking {
        val gate = CompletableDeferred<Unit>()
        var runs = 0
        val coordinator = coordinator(roundTrip = { _, _ ->
            runs++
            gate.await()
            EncoderRoundTrip.Outcome(ProbeOutcome.SUPPORTED, "Intact.")
        })

        val first = async { coordinator.probeEncoders(activity) }
        while (!coordinator.state.value.encoderProbeRunning) yield()
        val second = coordinator.probeEncoders(activity)
        assertEquals(ProbeOutcome.NOT_PROBED, second.roundTrip)
        gate.complete(Unit)
        assertEquals(ProbeOutcome.SUPPORTED, first.await().roundTrip)
        assertEquals(1, runs)
    }

    @Test
    fun `an answer from an earlier run is restored rather than asked for again`() = runBlocking {
        // The defect this pins: the flag saying the introduction had been read was persisted while
        // these answers were not, so from the second launch onward every streaming mode was Blocked
        // until somebody went to Settings and ran the check by hand.
        val store = RecordingStore(
            StoredCapabilities(
                encoders = setOf(CodecId.H264),
                encoderProbe = ProbeOutcome.SUPPORTED,
                virtualDisplayProbe = ProbeOutcome.SUPPORTED,
                encoderRoundTrip = ProbeOutcome.SUPPORTED,
                roundTripDetail = "Intact.",
                roundTripCodec = CodecId.H264,
            ),
        )
        val coordinator = coordinator(store = store)

        coordinator.restore()

        val probed = coordinator.state.value
        assertEquals(setOf(CodecId.H264), probed.encoders)
        assertEquals(ProbeOutcome.SUPPORTED, probed.encoderProbe)
        assertEquals(ProbeOutcome.SUPPORTED, probed.encoderRoundTrip)
        assertEquals(CodecId.H264, probed.roundTripCodec)
        assertTrue(probed.hasBeenAsked)
    }

    @Test
    fun `nothing stored leaves every answer outstanding`() = runBlocking {
        val coordinator = coordinator(store = RecordingStore(null))

        coordinator.restore()

        assertEquals(ProbeOutcome.NOT_PROBED, coordinator.state.value.encoderProbe)
        assertFalse(coordinator.state.value.hasBeenAsked)
    }

    @Test
    fun `a check run this session is not overwritten by an older stored answer`() = runBlocking {
        val store = RecordingStore(
            StoredCapabilities(
                encoders = setOf(CodecId.H265),
                encoderProbe = ProbeOutcome.SUPPORTED,
                virtualDisplayProbe = ProbeOutcome.SUPPORTED,
                encoderRoundTrip = ProbeOutcome.UNSUPPORTED,
                roundTripDetail = "From last week.",
                roundTripCodec = CodecId.H265,
            ),
        )
        val coordinator = coordinator(encoders = setOf(CodecId.H264), store = store)

        coordinator.probeEncoders(activity)
        coordinator.restore()

        val probed = coordinator.state.value
        assertEquals(setOf(CodecId.H264), probed.encoders, "the fresh answer must win")
        assertEquals(ProbeOutcome.SUPPORTED, probed.encoderRoundTrip)
    }

    @Test
    fun `both checks write their answers down`() = runBlocking {
        val store = RecordingStore()
        val coordinator = coordinator(store = store)

        coordinator.probeEncoders(activity)
        assertEquals(ProbeOutcome.SUPPORTED, assertNotNull(store.value).encoderProbe)

        coordinator.probeSecondScreen(activity)
        assertEquals(ProbeOutcome.SUPPORTED, assertNotNull(store.value).virtualDisplayProbe)
        assertEquals(2, store.saves)
    }
}
