package com.rextechnologies.flint.mobile.service

import android.content.Context
import android.content.pm.ServiceInfo
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.mobile.OutputMode
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals

@RunWith(AndroidJUnit4::class)
class CastServiceTest {
    @Test
    fun `a mirror runs as a projection and a second screen honestly does not`() {
        // Getting this wrong is not cosmetic. From API 34 a mediaProjection service without a
        // projection fails to start, and a projection cannot be taken without such a service -- so
        // a second screen declared as a projection would fail at start, and a mirror declared as
        // anything else would fail at consent.
        assertEquals(
            ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION,
            CastService.foregroundTypeFor(OutputMode.MIRROR),
        )
        assertEquals(
            ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE,
            CastService.foregroundTypeFor(OutputMode.SECOND_SCREEN),
        )
    }

    @Test
    fun `the start intent carries the mode and the device by name`() {
        val context: Context = ApplicationProvider.getApplicationContext()
        val intent = CastService.startIntent(context, OutputMode.MIRROR, "Fire TV")
        assertEquals(CastService.ACTION_START, intent.action)
        assertEquals("MIRROR", intent.getStringExtra(CastService.EXTRA_MODE))
        assertEquals("Fire TV", intent.getStringExtra(CastService.EXTRA_DEVICE_NAME))
        assertEquals(CastService.ACTION_STOP, CastService.stopIntent(context).action)
    }
}
