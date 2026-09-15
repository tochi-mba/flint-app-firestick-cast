package com.rextechnologies.flint.mobile.platform

import android.app.Activity
import android.os.Build
import android.os.Bundle
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.media.CodecChoice
import com.rextechnologies.flint.mobile.MobileActivity
import com.rextechnologies.flint.mobile.state.CapabilityCoordinator
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import org.junit.Assert.assertEquals
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.util.concurrent.atomic.AtomicReference

/**
 * The encoder round trip, on a device or an emulator.
 *
 * Not part of the pull-request gate, and it does not pretend to be: `MediaCodec` is real hardware and
 * a real driver, and a JVM cannot stand in for either. On a device with no hardware encoder the test
 * is skipped and reported as skipped, never as passed -- an unexecuted codec check is not evidence
 * of anything.
 *
 * The round trip is driven from the instrumentation thread. Running it inside `onActivity` would
 * block the main looper that has to draw the pattern, and the check would time out on every phone.
 */
@RunWith(AndroidJUnit4::class)
class EncoderRoundTripTest {
    @Test
    fun aTestFrameSurvivesTheProductionEncoder() {
        val encoders = PhoneProbes.probeHardwareEncoders()
        val codec = CodecChoice.preferred(encoders)
        assumeTrue("no hardware video encoder on this device; the round trip was not executed", codec != null)

        ActivityScenario.launch(MobileActivity::class.java).use { scenario ->
            val activity = AtomicReference<Activity>()
            scenario.onActivity { activity.set(it) }
            // A second run exercises teardown as well as allocation. The original whole-plane
            // copy crashed natively on the first frame of a physical SM-G998B (Android 15).
            repeat(3) { attempt ->
                val outcome = runBlocking {
                    EncoderRoundTrip.run(activity.get(), checkNotNull(codec), Build.VERSION.SDK_INT)
                }
                assertEquals(outcome.detail, ProbeOutcome.SUPPORTED, outcome.outcome)
                report("Encoder round trip ${attempt + 1}: ${outcome.detail}")
            }
        }
    }

    @Test
    fun capabilityChecksCanBeRepeatedAndRestored() {
        ActivityScenario.launch(MobileActivity::class.java).use { scenario ->
            val activity = AtomicReference<Activity>()
            scenario.onActivity { activity.set(it) }
            runBlocking {
                val phone = activity.get()
                val metrics = phone.resources.displayMetrics
                fun coordinator() = CapabilityCoordinator(
                    phone,
                    Build.MODEL,
                    metrics.widthPixels,
                    metrics.heightPixels,
                    metrics.densityDpi,
                )
                val checks = coordinator()
                repeat(2) { attempt ->
                    val display = withContext(Dispatchers.Main) { checks.probeSecondScreen(phone) }
                    assertEquals("Second-screen pixels", ProbeOutcome.SUPPORTED, display)
                    val encoder = withContext(Dispatchers.Main) { checks.probeEncoders(phone) }
                    assertEquals(encoder.detail, ProbeOutcome.SUPPORTED, encoder.roundTrip)
                    assertEquals(false, checks.state.value.encoderProbeRunning)
                    assertEquals(false, checks.state.value.secondScreenProbeRunning)
                    report("Capability check ${attempt + 1}: display=$display; ${encoder.detail}")
                }
                val restored = coordinator()
                restored.restore()
                assertEquals(checks.state.value, restored.state.value)
                report("Capability answers restored into a new coordinator.")
            }
        }
    }

    private fun report(detail: String) {
        InstrumentationRegistry.getInstrumentation().sendStatus(0, Bundle().apply { putString("stream", "$detail\n") })
    }
}
