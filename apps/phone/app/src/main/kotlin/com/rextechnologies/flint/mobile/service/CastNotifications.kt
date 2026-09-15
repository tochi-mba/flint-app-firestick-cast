package com.rextechnologies.flint.mobile.service

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import com.rextechnologies.flint.castcore.copy.NotificationCopy
import com.rextechnologies.flint.mobile.MobileActivity
import com.rextechnologies.flint.mobile.R

/**
 * The ongoing notification.
 *
 * It is not decoration. Once the phone is locked it is the only Stop control that exists, which is
 * why it carries the action rather than only the text, and why its importance is low: a control
 * somebody needs at the moment they reach for it should not have interrupted them four times before
 * then.
 */
internal class CastNotifications(private val context: Context) {
    private val manager: NotificationManager
        get() = context.getSystemService(NotificationManager::class.java)

    /**
     * A foreground service with no valid channel is killed on sight by newer Android, and nothing
     * about that failure looks like a notification problem.
     */
    fun ensureChannel() {
        manager.createNotificationChannel(
            NotificationChannel(
                NotificationCopy.CHANNEL_ID,
                NotificationCopy.CHANNEL_NAME,
                NotificationManager.IMPORTANCE_LOW,
            ).apply { description = NotificationCopy.CHANNEL_DESCRIPTION },
        )
    }

    fun ongoing(mode: String, deviceName: String): Notification {
        val open = PendingIntent.getActivity(
            context,
            0,
            Intent(context, MobileActivity::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val stop = PendingIntent.getService(
            context,
            1,
            Intent(context, CastService::class.java).setAction(CastService.ACTION_STOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        return NotificationCompat.Builder(context, NotificationCopy.CHANNEL_ID)
            // The frameless variant of the mark. A status bar renders this at 24dp, which is below
            // the size at which the framed one stays legible.
            .setSmallIcon(R.drawable.ic_flint_notification)
            .setContentTitle(NotificationCopy.title(mode))
            .setContentText(NotificationCopy.text(deviceName))
            .setContentIntent(open)
            .addAction(0, NotificationCopy.STOP_ACTION, stop)
            .setOngoing(true)
            // Without this every telemetry update re-alerts, and the receiver reports twice a second.
            .setOnlyAlertOnce(true)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .build()
    }

    fun update(mode: String, deviceName: String) =
        manager.notify(NOTIFICATION_ID, ongoing(mode, deviceName))

    companion object {
        const val NOTIFICATION_ID = 47
    }
}
