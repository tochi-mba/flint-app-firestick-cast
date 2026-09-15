package com.rextechnologies.flint.receiver.ui

import android.view.KeyEvent
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** The remote-control behaviour of the mirror surface's hidden controls. */
class MirrorOverlayTest {
    @Test
    fun `every dpad direction summons the controls when they are hidden`() {
        // The viewer has no way of knowing which direction is the magic one, so all of them are.
        val directions = listOf(
            KeyEvent.KEYCODE_DPAD_UP,
            KeyEvent.KEYCODE_DPAD_DOWN,
            KeyEvent.KEYCODE_DPAD_LEFT,
            KeyEvent.KEYCODE_DPAD_RIGHT,
        )

        for (keyCode in directions) {
            assertEquals(
                MirrorKeyOutcome.REVEAL_CONTROLS,
                mirrorKeyOutcome(keyCode, controlsVisible = false),
                "key code $keyCode should reveal the controls",
            )
        }
    }

    @Test
    fun `the select button summons the controls rather than doing nothing`() {
        // Pressing OK on a bare picture is the most obvious thing to try, and it used to do
        // nothing at all — the complaint that prompted this whole surface.
        assertEquals(
            MirrorKeyOutcome.REVEAL_CONTROLS,
            mirrorKeyOutcome(KeyEvent.KEYCODE_DPAD_CENTER, controlsVisible = false),
        )
        assertEquals(
            MirrorKeyOutcome.REVEAL_CONTROLS,
            mirrorKeyOutcome(KeyEvent.KEYCODE_ENTER, controlsVisible = false),
        )
    }

    @Test
    fun `menu and info summon the controls too`() {
        // Remotes vary in which of these they carry; a viewer pressing either means the same thing.
        assertEquals(
            MirrorKeyOutcome.REVEAL_CONTROLS,
            mirrorKeyOutcome(KeyEvent.KEYCODE_MENU, controlsVisible = false),
        )
        assertEquals(
            MirrorKeyOutcome.REVEAL_CONTROLS,
            mirrorKeyOutcome(KeyEvent.KEYCODE_INFO, controlsVisible = false),
        )
    }

    @Test
    fun `the dpad navigates between buttons once the controls are already up`() {
        // The reveal press is consumed, but every press after it belongs to focus navigation.
        // Swallowing these too would leave the controls visible and completely unusable.
        val directions = listOf(
            KeyEvent.KEYCODE_DPAD_UP,
            KeyEvent.KEYCODE_DPAD_DOWN,
            KeyEvent.KEYCODE_DPAD_LEFT,
            KeyEvent.KEYCODE_DPAD_RIGHT,
        )

        for (keyCode in directions) {
            assertEquals(
                MirrorKeyOutcome.IGNORE,
                mirrorKeyOutcome(keyCode, controlsVisible = true),
                "key code $keyCode should reach focus navigation",
            )
        }
    }

    @Test
    fun `select activates the focused button once the controls are up`() {
        // Otherwise the buttons would be reachable but never pressable.
        assertEquals(
            MirrorKeyOutcome.IGNORE,
            mirrorKeyOutcome(KeyEvent.KEYCODE_DPAD_CENTER, controlsVisible = true),
        )
    }

    @Test
    fun `back puts the controls away rather than ending the mirror`() {
        // The step-back-by-one rule: with the controls up, the thing to close is the controls.
        // Ending the session here would make a glance at the controls cost the whole mirror.
        assertEquals(
            MirrorKeyOutcome.HIDE_CONTROLS,
            mirrorKeyOutcome(KeyEvent.KEYCODE_BACK, controlsVisible = true),
        )
    }

    @Test
    fun `back ends the mirror once there are no controls left to close`() {
        assertEquals(
            MirrorKeyOutcome.STOP_MIRRORING,
            mirrorKeyOutcome(KeyEvent.KEYCODE_BACK, controlsVisible = false),
        )
    }

    @Test
    fun `menu toggles the controls back off`() {
        // A key that opens something should close it again; a menu button that only ever opened
        // would leave the viewer pressing it repeatedly with nothing happening.
        assertEquals(
            MirrorKeyOutcome.HIDE_CONTROLS,
            mirrorKeyOutcome(KeyEvent.KEYCODE_MENU, controlsVisible = true),
        )
        assertEquals(
            MirrorKeyOutcome.HIDE_CONTROLS,
            mirrorKeyOutcome(KeyEvent.KEYCODE_INFO, controlsVisible = true),
        )
    }

    @Test
    fun `keys the mirror has no use for are left alone in both states`() {
        // Volume must reach the system, and the media keys mean nothing on a mirror. Claiming
        // them would break the television's own controls for no gain.
        val notOurs = listOf(
            KeyEvent.KEYCODE_VOLUME_UP,
            KeyEvent.KEYCODE_VOLUME_DOWN,
            KeyEvent.KEYCODE_VOLUME_MUTE,
            KeyEvent.KEYCODE_HOME,
            KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE,
            KeyEvent.KEYCODE_MEDIA_FAST_FORWARD,
            KeyEvent.KEYCODE_SEARCH,
        )

        for (keyCode in notOurs) {
            assertEquals(
                MirrorKeyOutcome.IGNORE,
                mirrorKeyOutcome(keyCode, controlsVisible = false),
                "key code $keyCode should be left alone while the controls are hidden",
            )
            assertEquals(
                MirrorKeyOutcome.IGNORE,
                mirrorKeyOutcome(keyCode, controlsVisible = true),
                "key code $keyCode should be left alone while the controls are visible",
            )
        }
    }

    @Test
    fun `revealing is never the outcome while the controls are already visible`() {
        // Re-revealing would restart the auto-hide timer from a press meant for a button, so no
        // key may report it in this state. Checked across the whole key space rather than a list.
        for (keyCode in TEST_KEY_CODES) {
            assertTrue(
                mirrorKeyOutcome(keyCode, controlsVisible = true) != MirrorKeyOutcome.REVEAL_CONTROLS,
                "key code $keyCode must not reveal already-visible controls",
            )
        }
    }

    @Test
    fun `the mirror is never ended by a key press while the controls are visible`() {
        // Only BACK on a bare picture ends a session. Anything else would make an accidental
        // press while browsing the controls destroy the mirror.
        for (keyCode in TEST_KEY_CODES) {
            assertTrue(
                mirrorKeyOutcome(keyCode, controlsVisible = true) != MirrorKeyOutcome.STOP_MIRRORING,
                "key code $keyCode must not end the mirror while the controls are visible",
            )
        }
    }

    @Test
    fun `back is the only key that ends the mirror`() {
        for (keyCode in TEST_KEY_CODES) {
            if (keyCode == KeyEvent.KEYCODE_BACK) continue
            assertTrue(
                mirrorKeyOutcome(keyCode, controlsVisible = false) != MirrorKeyOutcome.STOP_MIRRORING,
                "key code $keyCode must not end the mirror",
            )
        }
    }

    @Test
    fun `fit toggles to fill and back again`() {
        assertEquals(MirrorFitMode.FILL, MirrorFitMode.FIT.toggled())
        assertEquals(MirrorFitMode.FIT, MirrorFitMode.FILL.toggled())
    }

    @Test
    fun `toggling twice returns to where it started for every mode`() {
        for (mode in MirrorFitMode.entries) {
            assertEquals(mode, mode.toggled().toggled(), "$mode should survive a round trip")
        }
    }

    @Test
    fun `the fit button offers the mode it would switch to rather than the current one`() {
        // A button labelled with the state you are already in reads as though pressing it does
        // nothing, which is how these controls get described as broken.
        assertEquals("FILL SCREEN", MirrorFitMode.FIT.actionLabel)
        assertEquals("FIT SCREEN", MirrorFitMode.FILL.actionLabel)
    }

    @Test
    fun `a reported frame size becomes an aspect ratio`() {
        assertEquals(16f / 9f, mirrorAspectRatio(1920, 1080))
        assertEquals(1.6f, mirrorAspectRatio(1280, 800))
    }

    @Test
    fun `an unreported frame size has no aspect ratio rather than a guessed one`() {
        // Guessing 16:9 here made the picture visibly jump when the true size arrived.
        assertNull(mirrorAspectRatio(0, 0))
        assertNull(mirrorAspectRatio(1920, 0))
        assertNull(mirrorAspectRatio(0, 1080))
    }

    @Test
    fun `a negative frame size is treated as unreported rather than inverting the picture`() {
        assertNull(mirrorAspectRatio(-1920, 1080))
        assertNull(mirrorAspectRatio(1920, -1080))
    }

    @Test
    fun `the stats row says it is waiting before any frame arrives`() {
        // Even when a size has been negotiated: a size is not a picture, and reporting one while
        // nothing decodes is precisely how a broken mirror looked healthy.
        assertEquals(
            "Waiting for the first frame",
            mirrorStatsLabel(1920, 1080, frameReceived = false),
        )
    }

    @Test
    fun `the stats row reports the live frame size once frames arrive`() {
        assertEquals("1920 x 1080", mirrorStatsLabel(1920, 1080, frameReceived = true))
    }

    @Test
    fun `frames arriving without a reported size say so rather than showing zeroes`() {
        assertEquals("Streaming, size not reported", mirrorStatsLabel(0, 0, frameReceived = true))
    }

    private companion object {
        // KeyEvent.getMaxKeyCode() is an Android runtime call and throws in this plain JVM suite.
        // This exceeds every Fire TV remote key code and also exercises the unknown-key branch.
        val TEST_KEY_CODES = 0..512
    }
}
