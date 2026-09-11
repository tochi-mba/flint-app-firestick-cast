package com.rextechnologies.flint.mobile

import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.core.view.WindowCompat
import com.rextechnologies.flint.design.FlintTheme
import com.rextechnologies.flint.mobile.ui.MobileApp

/**
 * The whole app's window.
 *
 * One activity, and it handles every configuration change itself rather than being recreated. That
 * is not a convenience: rotating the phone during a cast has to reconfigure the encoder and re-send
 * VIDEO_CONFIG, and a recreation would tear the session down underneath it — the television would
 * show a frozen frame and nobody would connect that to having turned the phone sideways.
 */
class MobileActivity : ComponentActivity() {
    private lateinit var controller: MobileController

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)

        controller = MobileController(this)
        val metrics = resources.displayMetrics
        controller.start(
            deviceName = Build.MODEL.orEmpty().ifBlank { "This phone" },
            screenWidth = metrics.widthPixels,
            screenHeight = metrics.heightPixels,
            densityDpi = metrics.densityDpi,
        )

        setContent {
            FlintTheme {
                MobileApp(controller = controller, activity = this)
            }
        }
    }

    override fun onResume() {
        super.onResume()
        // A hotspot that came up while the app was in the background is the ordinary case, not the
        // exception, so the verdict is re-sampled on the way back in rather than trusted from before.
        controller.refreshNetwork()
    }

    override fun onDestroy() {
        controller.stop()
        super.onDestroy()
    }
}
