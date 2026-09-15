package com.rextechnologies.flint.mobile.platform

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.After
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertSame
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
class CrashLogTest {
    private val context: Context = ApplicationProvider.getApplicationContext()

    @After
    fun clearReport() {
        CrashLog.clear(context)
    }

    @Test
    fun `reports keep code locations and exception types without exception messages`() {
        val failure = IllegalStateException("root-secret-fixture")
        val cause = IllegalArgumentException("cause-secret-fixture")
        failure.initCause(cause)
        cause.initCause(failure)
        failure.addSuppressed(UnsupportedOperationException("suppressed-secret-fixture"))

        CrashLog.record(context, failure, "the encoder check")

        val report = assertNotNull(CrashLog.last(context))
        assertTrue(report.contains("java.lang.IllegalStateException"))
        assertTrue(report.contains("Caused by: java.lang.IllegalArgumentException"))
        assertTrue(report.contains("Suppressed: java.lang.UnsupportedOperationException"))
        assertTrue(report.contains("CrashLogTest.kt:"))
        assertFalse(report.contains("secret-fixture"))
    }

    @Test
    fun `a large report stays bounded and can be cleared`() {
        val failure = IllegalStateException().apply {
            stackTrace = Array(1_000) { StackTraceElement("Codec", "decode", "Codec.kt", it) }
        }
        CrashLog.record(context, failure, "the encoder check")

        assertTrue(assertNotNull(CrashLog.last(context)).length <= 16 * 1024)
        CrashLog.clear(context)
        assertNull(CrashLog.last(context))
    }

    @Test
    fun `installing twice records a fatal failure and still calls the previous handler once`() {
        val original = Thread.getDefaultUncaughtExceptionHandler()
        val failure = IllegalStateException("secret-fixture")
        val thread = Thread.currentThread()
        var forwarded = 0
        try {
            Thread.setDefaultUncaughtExceptionHandler { receivedThread, receivedFailure ->
                forwarded++
                assertSame(thread, receivedThread)
                assertSame(failure, receivedFailure)
                assertTrue(assertNotNull(CrashLog.last(context)).contains("The app closed"))
            }
            CrashLog.install(context)
            val installed = Thread.getDefaultUncaughtExceptionHandler()
            CrashLog.install(context)
            assertSame(installed, Thread.getDefaultUncaughtExceptionHandler())

            assertNotNull(installed).uncaughtException(thread, failure)

            assertEquals(1, forwarded)
            assertFalse(assertNotNull(CrashLog.last(context)).contains("secret-fixture"))
        } finally {
            Thread.setDefaultUncaughtExceptionHandler(original)
        }
    }
}
