package com.rextechnologies.flint.mobile

import android.Manifest
import android.content.pm.PackageManager
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
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
 *
 * The two system dialogs live here rather than in the controller, because both are launched through
 * an `ActivityResultLauncher` and a launcher must be registered before the activity is started.
 */
class MobileActivity : ComponentActivity() {
    private lateinit var controller: MobileController

    /**
     * The capture consent dialog, which Android shows afresh for every mirror.
     *
     * It will not remember the answer, by design, so this is registered once and used every time
     * rather than gated on anything stored.
     */
    private val projectionConsent = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult(),
    ) { result ->
        controller.onProjectionConsent(result.resultCode, result.data)
    }

    /**
     * Notifications, asked for once.
     *
     * The manifest has declared this permission since the foreground service was written and nothing
     * ever asked for it, so on API 33 and above the ongoing notification was silently suppressed —
     * and that notification is the only Stop control a locked phone has.
     */
    private val notificationPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        controller.onNotificationPermission(granted)
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        WindowCompat.setDecorFitsSystemWindows(window, false)

        val metrics = resources.displayMetrics
        controller = MobileController(
            context = this,
            deviceName = Build.MODEL.orEmpty().ifBlank { "This phone" },
            screenWidth = metrics.widthPixels,
            screenHeight = metrics.heightPixels,
            densityDpi = metrics.densityDpi,
        )
        controller.start()
        requestNotificationPermissionOnce()

        setContent {
            FlintTheme {
                MobileApp(
                    controller = controller,
                    activity = this,
                    onRequestMirror = ::requestMirrorConsent,
                )
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
        controller.close()
        super.onDestroy()
    }

    /**
     * Asks Android whether Flint may record this screen.
     *
     * The sentence explaining why is on the card behind this, shown before the dialog rather than
     * after it — Android asks every session and cannot remember the answer, so the app that explains
     * itself is the one a person is less annoyed by the fourth time.
     */
    private fun requestMirrorConsent() {
        val manager = getSystemService(MediaProjectionManager::class.java)
        if (manager == null) {
            controller.onProjectionConsent(RESULT_CANCELED, null)
            return
        }
        projectionConsent.launch(manager.createScreenCaptureIntent())
    }

    private fun requestNotificationPermissionOnce() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return
        val already = ContextCompat.checkSelfPermission(
            this,
            Manifest.permission.POST_NOTIFICATIONS,
        ) == PackageManager.PERMISSION_GRANTED
        if (already) return
        notificationPermission.launch(Manifest.permission.POST_NOTIFICATIONS)
    }
}
