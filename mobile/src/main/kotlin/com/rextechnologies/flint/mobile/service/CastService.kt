package com.rextechnologies.flint.mobile.service

import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import androidx.core.content.ContextCompat
import com.rextechnologies.flint.castcore.copy.ScreenCopy
import com.rextechnologies.flint.mobile.OutputMode
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Keeps a session alive while the app is not in front.
 *
 * Two service types are declared, and which one it starts with is decided by what the session is
 * actually doing. A mirror captures the screen and must run as `mediaProjection` — on API 34 and
 * above the projection cannot even be obtained before such a service is running. A second screen
 * captures nothing at all: it draws into a display this app owns, and declaring that as a projection
 * would be a claim about what the app is doing that is not true, so it runs as `connectedDevice`
 * instead, which is what it is.
 */
class CastService : Service() {
    private lateinit var notifications: CastNotifications
    private var wakeLock: PowerManager.WakeLock? = null

    override fun onCreate() {
        super.onCreate()
        notifications = CastNotifications(this)
        notifications.ensureChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> {
                stopSelfSafely()
                return START_NOT_STICKY
            }

            ACTION_START -> startOutput(intent)
            else -> stopSelfSafely()
        }
        return START_NOT_STICKY
    }

    private fun startOutput(intent: Intent) {
        val mode = runCatching {
            OutputMode.valueOf(intent.getStringExtra(EXTRA_MODE).orEmpty())
        }.getOrDefault(OutputMode.NONE)
        if (mode == OutputMode.NONE) {
            stopSelfSafely()
            return
        }

        val deviceName = intent.getStringExtra(EXTRA_DEVICE_NAME).orEmpty().ifBlank { "the TV" }
        val label = when (mode) {
            OutputMode.MIRROR -> ScreenCopy.MIRROR_TITLE
            OutputMode.SECOND_SCREEN -> ScreenCopy.SECOND_SCREEN_TITLE
            OutputMode.NONE -> ScreenCopy.MIRROR_TITLE
        }

        val notification = notifications.ongoing(label, deviceName)
        // The typed overload exists from API 29. Below that the manifest's declaration is the whole
        // story, and this app's floor is 26.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(CastNotifications.NOTIFICATION_ID, notification, foregroundTypeFor(mode))
        } else {
            startForeground(CastNotifications.NOTIFICATION_ID, notification)
        }

        acquireWakeLock(label)
        foreground.value = true
    }

    /**
     * Held only while something is being sent.
     *
     * A partial lock rather than a screen one: the phone's own screen may sleep during a second
     * screen, because nothing is reading it. The mirror path additionally asks the activity to keep
     * the screen on, because a mirror of a sleeping screen is a black rectangle.
     */
    private fun acquireWakeLock(tag: String) {
        if (wakeLock != null) return
        val power = getSystemService(PowerManager::class.java) ?: return
        wakeLock = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "flint:$tag").apply {
            setReferenceCounted(false)
            runCatching { acquire(WAKE_LOCK_TIMEOUT_MILLIS) }
        }
    }

    private fun stopSelfSafely() {
        runCatching { wakeLock?.release() }
        wakeLock = null
        foreground.value = false
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    override fun onDestroy() {
        runCatching { wakeLock?.release() }
        wakeLock = null
        foreground.value = false
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    companion object {
        /**
         * The type this session honestly is.
         *
         * Getting this wrong is not cosmetic. On API 34 and above, starting a `mediaProjection` service
         * without an active projection throws, and asking for a projection before such a service is
         * running also throws — so a second screen declared as a projection would fail at start, and a
         * mirror declared as anything else would fail at consent.
         */
            internal fun foregroundTypeFor(mode: OutputMode): Int = when (mode) {
            OutputMode.MIRROR -> ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
            OutputMode.SECOND_SCREEN, OutputMode.NONE ->
                ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE
        }

        /**
         * Whether this service is genuinely in the foreground.
         *
         * Published because the mirror has to wait for it. From API 34 a `MediaProjection` cannot be
         * obtained unless a `mediaProjection`-typed foreground service is already running, so
         * "asked the system to start it" is not good enough -- the projection is taken only once
         * `startForeground` has actually returned.
         */
        private val foreground = MutableStateFlow(false)

        val isForeground: StateFlow<Boolean> = foreground

        /** Waits for [isForeground], and says whether it arrived rather than throwing when it does not. */
        suspend fun awaitForeground(timeoutMillis: Long = FOREGROUND_TIMEOUT_MILLIS): Boolean =
            withTimeoutOrNull(timeoutMillis) { foreground.first { it } } ?: false

        /** Starts the service for [mode]. The system requires the foreground variant here. */
        fun start(context: Context, mode: OutputMode, deviceName: String) {
            ContextCompat.startForegroundService(context, startIntent(context, mode, deviceName))
        }

        /** Stops it. An ordinary start, because a service being asked to stop is not urgent. */
        fun stop(context: Context) {
            runCatching { context.startService(stopIntent(context)) }
        }

        /** Long enough for a cold service start on a slow phone, short enough to report rather than hang. */
        const val FOREGROUND_TIMEOUT_MILLIS: Long = 5_000

        const val ACTION_START = "com.rextechnologies.flint.mobile.START"
        const val ACTION_STOP = "com.rextechnologies.flint.mobile.STOP"
        const val EXTRA_MODE = "mode"
        const val EXTRA_DEVICE_NAME = "device"

        /**
         * A bounded lock, rather than one held until something remembers to release it.
         *
         * Four hours is longer than any session anybody will sit through and short enough that a
         * crash cannot flatten a battery overnight.
         */
        const val WAKE_LOCK_TIMEOUT_MILLIS = 4L * 60 * 60 * 1_000

        fun startIntent(context: Context, mode: OutputMode, deviceName: String): Intent =
            Intent(context, CastService::class.java)
                .setAction(ACTION_START)
                .putExtra(EXTRA_MODE, mode.name)
                .putExtra(EXTRA_DEVICE_NAME, deviceName)

        fun stopIntent(context: Context): Intent =
            Intent(context, CastService::class.java).setAction(ACTION_STOP)
    }
}
