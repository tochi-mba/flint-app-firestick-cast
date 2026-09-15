package com.rextechnologies.flint.mobile.platform

import android.app.Activity
import android.os.Build
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.castcore.media.CodecChoice
import com.rextechnologies.flint.mobile.MobileActivity
import kotlinx.coroutines.runBlocking
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
            val outcome = runBlocking {
                EncoderRoundTrip.run(activity.get(), checkNotNull(codec), Build.VERSION.SDK_INT)
            }
            assertEquals(outcome.detail, ProbeOutcome.SUPPORTED, outcome.outcome)
        }
    }
}
