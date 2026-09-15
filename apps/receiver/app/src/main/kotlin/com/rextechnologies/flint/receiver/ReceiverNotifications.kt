package com.rextechnologies.flint.receiver

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationCompat

/**
 * The receiver's ongoing notification.
 *
 * Split out of the service because it is a complete responsibility on its own — a channel, a
 * builder, and one line of text — that shares nothing with decoding, mirroring or browsing beyond
 * the address and pairing code it prints. It is also the part of the service most easily got wrong
 * silently: a foreground service without a valid channel is killed on sight by newer Android, and
 * nothing about that failure looks like a notification bug.
 */
internal class ReceiverNotifications(private val context: Context) {
    private val manager: NotificationManager
        get() = context.getSystemService(NotificationManager::class.java)

    /**
     * Creates the channel this notification posts to.
     *
     * Must run before the first `startForeground`. Channels are only a concept from API 26, and
     * this receiver still supports the Fire OS 6 devices that report API 25.
     */
    fun ensureChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            manager.createNotificationChannel(
                NotificationChannel(CHANNEL_ID, CHANNEL_NAME, NotificationManager.IMPORTANCE_LOW),
            )
        }
    }

    /** Builds the ongoing notification carrying [text]. */
    fun ongoing(text: String): Notification {
        val pending = PendingIntent.getActivity(
            context,
            0,
            Intent(context, ReceiverActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        return NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_launcher_foreground)
            .setContentTitle(TITLE)
            .setContentText(text)
            .setContentIntent(pending)
            .setOngoing(true)
            // Without this every state report re-alerts, and the receiver reports twice a second.
            .setOnlyAlertOnce(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
    }

    /** Replaces the posted notification's text. */
    fun update(text: String) = manager.notify(NOTIFICATION_ID, ongoing(text))

    companion object {
        const val CHANNEL_ID = "rexcast_receiver"
        const val CHANNEL_NAME = "Receiver status"
        const val NOTIFICATION_ID = 42
        const val TITLE = "Flint Receiver"
    }
}

/**
 * The one line of text the notification shows.
 *
 * Everything needed to reach this receiver from a desktop, in the order someone types it: where it
 * is, and the code that lets them in. Pulled out as a pure function because it is the only part of
 * the notification worth asserting on, and asserting on it does not need an Android framework.
 */
internal fun receiverNotificationText(state: ReceiverUiState): String =
    state.address?.let { "$it:${state.port} · code ${state.pairingCode}" }
        ?: "Waiting for the hotspot network"
