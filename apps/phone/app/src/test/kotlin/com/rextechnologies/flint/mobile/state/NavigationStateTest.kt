package com.rextechnologies.flint.mobile.state

import android.content.Context
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.castcore.copy.MobileTab
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.yield
import org.junit.Test
import org.junit.runner.RunWith
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

@RunWith(AndroidJUnit4::class)
class NavigationStateTest {
    private val context: Context = ApplicationProvider.getApplicationContext()

    private fun navigation() = NavigationState(context, CoroutineScope(Dispatchers.Unconfined))

    private fun NavigationState.awaitIntroductionAnswer(): Boolean = runBlocking {
        withTimeout(2_000) {
            while (state.value.introductionSeen == null) yield()
            state.value.introductionSeen!!
        }
    }

    @Test
    fun `the introduction is unknown until read, then unseen on a fresh install`() {
        context.getSharedPreferences("flint-mobile-navigation", Context.MODE_PRIVATE).edit().clear().commit()
        val navigation = navigation()
        assertFalse(navigation.awaitIntroductionAnswer())
    }

    @Test
    fun `seeing the introduction is remembered across a cold start`() {
        context.getSharedPreferences("flint-mobile-navigation", Context.MODE_PRIVATE).edit().clear().commit()
        navigation().apply {
            awaitIntroductionAnswer()
            markIntroductionSeen()
        }
        runBlocking {
            withTimeout(2_000) {
                while (!context.getSharedPreferences("flint-mobile-navigation", Context.MODE_PRIVATE)
                        .getBoolean("introduction-seen", false)
                ) {
                    yield()
                }
            }
        }
        assertTrue(navigation().awaitIntroductionAnswer())
    }

    @Test
    fun `replaying the introduction does not forget that it was seen`() {
        val navigation = navigation()
        navigation.markIntroductionSeen()
        navigation.replayIntroduction()
        assertEquals(false, navigation.state.value.introductionSeen)
        assertEquals(MobileTab.CAST, navigation.state.value.tab)
        assertTrue(navigation().awaitIntroductionAnswer(), "a replay wrote 'unseen' to disk")
    }

    @Test
    fun `back closes the sheet, then the notice, then returns to cast, and only then leaves`() {
        val navigation = navigation()
        navigation.selectTab(MobileTab.SETTINGS)
        navigation.notice("Something happened.")
        navigation.showPairing(true)

        assertTrue(navigation.onBack())
        assertFalse(navigation.state.value.pairingVisible)
        assertTrue(navigation.onBack())
        assertNull(navigation.state.value.notice)
        assertTrue(navigation.onBack())
        assertEquals(MobileTab.CAST, navigation.state.value.tab)
        assertFalse(navigation.onBack(), "on the Cast tab with nothing open, back belongs to the system")
    }

    @Test
    fun `a notice replaces the last one and a blank notice is no notice`() {
        val navigation = navigation()
        navigation.notice("First.")
        navigation.notice("Second.")
        assertEquals("Second.", navigation.state.value.notice)
        navigation.notice("   ")
        assertNull(navigation.state.value.notice)
    }

    @Test
    fun `opening or closing the sheet clears what was typed wrong last time`() {
        val navigation = navigation()
        navigation.showPairing(true)
        navigation.pairingError("Enter the six-digit code shown on the TV.")
        assertEquals("Enter the six-digit code shown on the TV.", navigation.state.value.pairingError)
        navigation.showPairing(false)
        assertNull(navigation.state.value.pairingError)
    }
}
