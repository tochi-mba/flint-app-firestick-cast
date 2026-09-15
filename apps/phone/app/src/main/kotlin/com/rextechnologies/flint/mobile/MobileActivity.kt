package com.rextechnologies.flint.mobile

import android.Manifest
import android.content.pm.PackageManager
import android.content.res.Configuration
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.ContextCompat
import androidx.core.view.WindowCompat
import com.rextechnologies.flint.design.FlintTheme
import com.rextechnologies.flint.mobile.platform.CrashLog
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

    /**
     * The record permission, which is what Android puts playback capture behind.
     *
     * Asked for on the way to the capture dialog rather than at launch, because it is only sound on
     * a mirror that needs it, and a permission asked for before the person has seen why is the one
     * they refuse. Refusing it is fine: the mirror carries the picture and the strip says why not
     * the sound.
     */
    private val audioPermission = registerForActivityResult(
        ActivityResultContracts.RequestPermission(),
    ) { granted ->
        controller.onAudioPermission(granted)
        launchMirrorConsent()
    }

    /**
     * The system's own file picker, which is the only way this app ever sees a file.
     *
     * Flint never browses storage. The picker hands back a content URI and a read grant, and the
     * bytes are streamed through the resolver straight to the socket; no path is ever resolved.
     */
    private val videoPicker = registerForActivityResult(
        ActivityResultContracts.OpenDocument(),
    ) { uri ->
        uri?.let { controller.chooseVideo(it.toString()) }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        // Before anything else that could fail. This app runs on phones its authors cannot reach,
        // so a crash that leaves no record behind is a crash nobody can fix.
        CrashLog.install(this)
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
                    onPickVideo = { videoPicker.launch(arrayOf(VIDEO_MIME)) },
                )
            }
        }
    }

    /**
     * The other half of the manifest's promise.
     *
     * Declaring that this activity handles configuration changes keeps a rotation from recreating
     * it; it does not, by itself, do anything about the mirror that is running. This is where the
     * new geometry reaches the encoder, and a mirror that did not follow the phone round was the
     * visible result of declaring one without the other.
     */
    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        val metrics = resources.displayMetrics
        controller.onDisplayChanged(metrics.widthPixels, metrics.heightPixels, metrics.densityDpi)
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
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val recording = ContextCompat.checkSelfPermission(this, Manifest.permission.RECORD_AUDIO)
            if (recording != PackageManager.PERMISSION_GRANTED) {
                audioPermission.launch(Manifest.permission.RECORD_AUDIO)
                return
            }
            controller.onAudioPermission(true)
        } else {
            controller.onAudioPermission(false)
        }
        launchMirrorConsent()
    }

    private fun launchMirrorConsent() {
        val manager = getSystemService(MediaProjectionManager::class.java)
        if (manager == null) {
            controller.onProjectionConsent(RESULT_CANCELED, null)
            return
        }
        projectionConsent.launch(manager.createScreenCaptureIntent())
    }

    private companion object {
        const val VIDEO_MIME = "video/*"
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
