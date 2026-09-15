package com.rextechnologies.flint.mobile.platform

import android.content.Context
import android.content.SharedPreferences
import android.os.Build
import com.rextechnologies.flint.mobile.BuildConfig
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import java.util.Collections
import java.util.IdentityHashMap

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

    /** Includes failures recorded while Settings is already open. */
    fun observe(context: Context): Flow<String?> = callbackFlow {
        val application = context.applicationContext
        val preferences = preferences(application)
        val listener = SharedPreferences.OnSharedPreferenceChangeListener { _, key ->
            if (key == KEY || key == null) trySend(last(application))
        }
        preferences.registerOnSharedPreferenceChangeListener(listener)
        trySend(last(application))
        awaitClose { preferences.unregisterOnSharedPreferenceChangeListener(listener) }
    }.distinctUntilChanged()

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
            runCatching { write(context, describe(failure, "a Flint thread", fatal = true)) }
            previous?.uncaughtException(thread, failure)
        }
    }

    private fun describe(failure: Throwable, where: String, fatal: Boolean): String {
        val heading = if (fatal) "The app closed on $where." else "A failure on $where was caught."
        return buildString {
            appendLine(heading)
            appendLine(
                "Flint ${BuildConfig.VERSION_NAME}, Android ${Build.VERSION.RELEASE} " +
                    "(API ${Build.VERSION.SDK_INT}), ${Build.MANUFACTURER} ${Build.MODEL}.",
            )
            appendLine()
            // Exception messages may contain URLs, tokens, file contents or credentials. Keep
            // exception types and code locations, including causes, without copying those values.
            val seen = Collections.newSetFromMap(IdentityHashMap<Throwable, Boolean>())
            appendFailure(failure, "", seen)
        }.take(MAXIMUM_CHARACTERS)
    }

    private fun StringBuilder.appendFailure(failure: Throwable, prefix: String, seen: MutableSet<Throwable>) {
        if (length >= MAXIMUM_CHARACTERS || seen.size >= MAXIMUM_FAILURES || !seen.add(failure)) return
        append(prefix).appendLine(failure.javaClass.name)
        for (frame in failure.stackTrace) {
            if (length >= MAXIMUM_CHARACTERS) return
            append("    at ").appendLine(frame)
        }
        failure.cause?.let { appendFailure(it, "Caused by: ", seen) }
        for (suppressed in failure.suppressed) appendFailure(suppressed, "Suppressed: ", seen)
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
    private const val MAXIMUM_FAILURES = 32
}
