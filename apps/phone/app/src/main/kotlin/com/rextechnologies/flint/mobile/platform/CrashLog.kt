package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.content.SharedPreferences
import android.os.Build
import com.rextechnologies.flint.mobile.BuildConfig
import java.io.PrintWriter
import java.io.StringWriter

/**
 * What the app has to say about the last time it failed.
 *
 * Nobody developing this app can reach the phones it runs on. There is no store listing, no crash
 * service and no way to read another app's logcat without a computer attached — so when the app
 * closes on somebody's phone, the only record of why is the one it keeps for itself. Without this,
 * a fault found on hardware is diagnosed by guessing, and every guess costs the person holding the
 * phone a rebuild and a reinstall.
 *
 * Two things are recorded, most recent only: a failure that killed the process, caught on the way
 * down by [install], and a failure that was survived, handed over by [record]. Neither is sent
 * anywhere — it is written to this app's own preferences and shown in Settings for a person to read
 * or copy, and it goes no further unless they choose to pass it on.
 */
object CrashLog {
    /**
     * Starts recording crashes. Safe to call more than once.
     *
     * Chains rather than replaces: whatever handler was already installed still runs afterwards, so
     * the platform's own reporting and the system's "app keeps stopping" dialog behave exactly as
     * they did before. This only takes a copy on the way past.
     */
    fun install(context: Context) {
        val application = context.applicationContext
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        if (previous is Chaining) return
        Thread.setDefaultUncaughtExceptionHandler(Chaining(application, previous))
    }

    /** Records a failure the app survived, so a caught fault is not invisible either. */
    fun record(context: Context, failure: Throwable, where: String) {
        runCatching { write(context.applicationContext, describe(failure, where, fatal = false)) }
    }

    /** The last recorded failure, or `null` when there has not been one. */
    fun last(context: Context): String? =
        runCatching {
            preferences(context.applicationContext).getString(KEY, null)?.takeIf { it.isNotBlank() }
        }.getOrNull()

    fun clear(context: Context) {
        runCatching { preferences(context.applicationContext).edit().remove(KEY).apply() }
    }

    private class Chaining(
        private val context: Context,
        private val previous: Thread.UncaughtExceptionHandler?,
    ) : Thread.UncaughtExceptionHandler {
        override fun uncaughtException(thread: Thread, failure: Throwable) {
            // Swallowed deliberately. The process is already going down; a recorder that threw on
            // the way would replace the report with its own and leave nothing behind.
            runCatching { write(context, describe(failure, "the ${thread.name} thread", fatal = true)) }
            previous?.uncaughtException(thread, failure)
        }
    }

    private fun describe(failure: Throwable, where: String, fatal: Boolean): String {
        val trace = StringWriter().also { failure.printStackTrace(PrintWriter(it)) }.toString()
        val heading = if (fatal) "The app closed on $where." else "A failure on $where was caught."
        return buildString {
            appendLine(heading)
            appendLine(
                "Flint ${BuildConfig.VERSION_NAME}, Android ${Build.VERSION.RELEASE} " +
                    "(API ${Build.VERSION.SDK_INT}), ${Build.MANUFACTURER} ${Build.MODEL}.",
            )
            appendLine()
            append(trace)
        }.take(MAXIMUM_CHARACTERS)
    }

    /**
     * Written synchronously.
     *
     * `apply()` would queue the write and return, which is right everywhere except here: the caller
     * on the fatal path is a process that is about to stop existing, and a queued write does not
     * survive that.
     */
    private fun write(context: Context, text: String) {
        preferences(context).edit().putString(KEY, text).commit()
    }

    private fun preferences(context: Context): SharedPreferences =
        context.getSharedPreferences(PREFERENCES_NAME, Context.MODE_PRIVATE)

    private const val PREFERENCES_NAME = "flint-mobile-crash"
    private const val KEY = "last"

    /** Long enough for any stack this app can produce, short enough not to stall a dying process. */
    private const val MAXIMUM_CHARACTERS = 16 * 1024
}
