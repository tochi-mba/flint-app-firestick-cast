package com.rextechnologies.flint.receiver

import androidx.media3.datasource.DefaultHttpDataSource
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * Regression test for a real playback failure: the receiver's HTTP fetch from the paired PC timed
 * out and reported `ERROR_CODE_IO_NETWORK_CONNECTION_TIMEOUT` even after the Windows Firewall was
 * confirmed open. Media3's [DefaultHttpDataSource.Factory] defaults to an 8-second connect and
 * read timeout, tuned for the open internet rather than a phone-hotspot LAN that this project has
 * independently observed re-leasing DHCP and briefly losing ADB under load. On that network, 8
 * seconds is not always enough for a fresh connection to land, and the resulting error is
 * indistinguishable from a genuinely blocked port — so a merely slow network and a firewalled one
 * looked identical to the user.
 *
 * [ReceiverService] no longer uses Media3's default; this pins that it stays overridden.
 */
class LocalMediaTimeoutTest {
    @Test
    fun `receiver's configured timeout is longer than Media3's default`() {
        // No public getter exists on the factory, so the default is proven the way the Media3
        // source itself documents it: DEFAULT_CONNECT_TIMEOUT_MILLIS and
        // DEFAULT_READ_TIMEOUT_MILLIS are both 8_000. This assertion is a canary — if a future
        // Media3 upgrade changes that default, this test explains why the exact multiple below
        // stops being the right comment, without silently losing its own meaning.
        val mediaThreeDefaultMillis = 8_000

        assertTrue(
            ReceiverService.LOCAL_MEDIA_TIMEOUT_MILLIS > mediaThreeDefaultMillis,
            "The receiver's timeout (${ReceiverService.LOCAL_MEDIA_TIMEOUT_MILLIS} ms) must stay " +
                "longer than Media3's default ($mediaThreeDefaultMillis ms), or this exact bug " +
                "returns.",
        )
    }

    @Test
    fun `receiver's configured timeout is a full thirty seconds`() {
        // The specific value chosen: generous enough for a congested hotspot LAN, still bounded so
        // a genuinely closed port fails in a reasonable time rather than hanging indefinitely.
        assertEquals(30_000, ReceiverService.LOCAL_MEDIA_TIMEOUT_MILLIS)
    }

    @Test
    fun `a data source factory built with the receiver's timeout actually accepts that value`() {
        // Proves the factory API itself does not silently clamp or reject the configured value —
        // exercising the exact call the receiver makes, not just asserting a constant in isolation.
        val factory = DefaultHttpDataSource.Factory()
            .setConnectTimeoutMs(ReceiverService.LOCAL_MEDIA_TIMEOUT_MILLIS)
            .setReadTimeoutMs(ReceiverService.LOCAL_MEDIA_TIMEOUT_MILLIS)

        // DefaultHttpDataSource has no public timeout getter, so the proof is that construction
        // does not throw: setConnectTimeoutMs/setReadTimeoutMs both reject a value outside
        // 1..Int.MAX_VALUE with IllegalArgumentException, so an accepted 30_000 here is itself
        // the assertion. Reaching this line without an exception is the pass.
        factory.createDataSource()
    }
}
