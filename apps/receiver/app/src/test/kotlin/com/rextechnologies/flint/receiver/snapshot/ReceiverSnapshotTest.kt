package com.rextechnologies.flint.receiver.snapshot

import androidx.activity.ComponentActivity
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.test.junit4.v2.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.requestFocus
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.rextechnologies.flint.protocol.wire.PlaybackState
import com.rextechnologies.flint.protocol.wire.SurfaceMode
import com.rextechnologies.flint.receiver.BrowserSurfaceUi
import com.rextechnologies.flint.receiver.ReceiverNetworkState
import com.rextechnologies.flint.receiver.ReceiverUiState
import com.rextechnologies.flint.receiver.ui.ReceiverBrowserChrome
import com.rextechnologies.flint.receiver.ui.ReceiverSurface
import com.rextechnologies.flint.receiver.ui.ReceiverTags
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config

/**
 * Every state the receiver can show on a television, held to an approved image.
 *
 * This suite exists because of a specific failure earlier in this project: the receiver was checked
 * on an emulator, looked correct, and then looked wrong on the actual Fire TV, because the screen
 * geometry is nothing alike. Rendering at the television's real resolution is the point — a
 * receiver layout verified at phone size proves nothing about the panel it runs on.
 *
 * What these images are asked to show, which no assertion in the receiver's other tests can:
 * text large enough to read from across a room, focus outlines visible at that distance, and
 * content inside the overscan-safe margin rather than under the bezel.
 */
@RunWith(AndroidJUnit4::class)
// A 1080p television, not a phone. 960x540dp at xhdpi is exactly 1920x1080 pixels, and the
// `television` qualifier is what makes Android apply the TV resource set and the leanback theme —
// so this renders the configuration a Fire TV actually reports rather than an approximation of it.
@Config(qualifiers = "w960dp-h540dp-television-xhdpi")
class ReceiverSnapshotTest {
    @get:Rule
    val compose = createAndroidComposeRule<ComponentActivity>()

    @Before
    fun takeControlOfTheClock() {
        // Set before any content is composed. The receiver's idle screen animates forever, and an
        // auto-advancing clock means the composition never reaches idle — every capture would race
        // the spinner and no two runs would agree.
        compose.mainClock.autoAdvance = false
    }

    @Test
    fun `the idle surface while the receiver is still starting`() {
        // The first thing on screen after launch, and the state a viewer sees if the network never
        // comes up. It has to explain itself rather than sit blank.
        compose.setContent {
            Surface(ReceiverUiState(networkState = ReceiverNetworkState.STARTING))
        }

        Snapshot.matches(compose, "receiver-starting")
    }

    @Test
    fun `the idle surface waiting for a network`() {
        compose.setContent {
            Surface(
                ReceiverUiState(
                    networkState = ReceiverNetworkState.WAITING,
                    detail = "Waiting for the network",
                ),
            )
        }

        Snapshot.matches(compose, "receiver-waiting")
    }

    @Test
    fun `the pairing screen a viewer reads the code off`() {
        // The screen that matters most: someone is across the room copying six digits onto a
        // laptop. If the code is small, low contrast, or outside the safe area, the product does
        // not work, and no property assertion would say so.
        compose.setContent { Surface(listening()) }

        Snapshot.matches(compose, "receiver-pairing")
    }

    @Test
    fun `the pairing screen once a host has connected`() {
        compose.setContent { Surface(listening().copy(peerName = "TOCHI-LAPTOP")) }

        Snapshot.matches(compose, "receiver-connected")
    }

    @Test
    fun `the error state names what went wrong`() {
        // A receiver that fails silently is the worst outcome, because the person is standing at a
        // television with no way to see a log.
        compose.setContent {
            Surface(listening().copy(error = "The host closed the connection unexpectedly."))
        }

        Snapshot.matches(compose, "receiver-error")
    }

    @Test
    fun `the playback surface with its heads-up display showing`() {
        compose.setContent {
            Surface(
                listening().copy(
                    peerName = "TOCHI-LAPTOP",
                    surfaceMode = SurfaceMode.PLAYER,
                    title = "new_Adobe 2026_VPro_blade3_1160x870.mp4",
                    playbackState = PlaybackState.PLAYING,
                    positionMs = 42_000,
                    durationMs = 187_000,
                ),
                // Never hidden: an auto-hiding overlay would race the capture and produce an image
                // that differs between runs for no reason to do with the layout.
                playbackHudAutoHideMillis = null,
                playerContent = { VideoStandIn() },
            )
        }

        Snapshot.matches(compose, "receiver-playback-hud")
    }

    @Test
    fun `the playback surface while paused`() {
        compose.setContent {
            Surface(
                listening().copy(
                    peerName = "TOCHI-LAPTOP",
                    surfaceMode = SurfaceMode.PLAYER,
                    title = "new_Adobe 2026_VPro_blade3_1160x870.mp4",
                    playbackState = PlaybackState.PAUSED,
                    positionMs = 42_000,
                    durationMs = 187_000,
                ),
                playbackHudAutoHideMillis = null,
                playerContent = { VideoStandIn() },
            )
        }

        Snapshot.matches(compose, "receiver-playback-paused")
    }

    @Test
    fun `the playback surface reporting an error over the video`() {
        compose.setContent {
            Surface(
                listening().copy(
                    peerName = "TOCHI-LAPTOP",
                    surfaceMode = SurfaceMode.PLAYER,
                    title = "holiday.mkv",
                    playbackState = PlaybackState.IDLE,
                    error = "This file uses a codec this Fire TV cannot decode.",
                ),
                playbackHudAutoHideMillis = null,
                playerContent = { VideoStandIn() },
            )
        }

        Snapshot.matches(compose, "receiver-playback-error")
    }

    @Test
    fun `the mirror surface before the first frame arrives`() {
        // The window where a viewer is most likely to think it has failed, so it must say what it
        // is waiting for rather than show a black screen.
        compose.setContent {
            Surface(
                listening().copy(
                    peerName = "TOCHI-LAPTOP",
                    surfaceMode = SurfaceMode.MIRROR,
                    mirrorWidth = 1920,
                    mirrorHeight = 1080,
                    mirrorFrameReceived = false,
                ),
                mirrorContent = { VideoStandIn() },
            )
        }

        Snapshot.matches(compose, "receiver-mirror-waiting")
    }

    @Test
    fun `the mirror surface with its controls summoned`() {
        // Summoned by the remote, which is the only way to reach them. Held open for the capture
        // for the same reason the playback overlay is.
        compose.setContent {
            Surface(
                listening().copy(
                    peerName = "TOCHI-LAPTOP",
                    surfaceMode = SurfaceMode.MIRROR,
                    mirrorWidth = 1920,
                    mirrorHeight = 1080,
                    mirrorFrameReceived = true,
                ),
                mirrorControlsAutoHideMillis = null,
                initialMirrorControlsVisible = true,
                mirrorContent = { VideoStandIn() },
            )
        }

        Snapshot.matches(compose, "receiver-mirror-controls")
    }

    @Test
    fun `the mirror surface with the controls hidden`() {
        compose.setContent {
            Surface(
                listening().copy(
                    peerName = "TOCHI-LAPTOP",
                    surfaceMode = SurfaceMode.MIRROR,
                    mirrorWidth = 1920,
                    mirrorHeight = 1080,
                    mirrorFrameReceived = true,
                ),
                mirrorControlsAutoHideMillis = null,
                initialMirrorControlsVisible = false,
                mirrorContent = { VideoStandIn() },
            )
        }

        Snapshot.matches(compose, "receiver-mirror-clean")
    }

    @Test
    fun `a focused mirror control shows an outline a viewer can see from a sofa`() {
        // The complaint this pins, in the reporter's own words: "there are no outlines when I move
        // around to select options on the bottom menu". A remote user navigates entirely by focus,
        // so a control that does not visibly change when focused makes the whole surface unusable —
        // and nothing but a rendered image can show whether the change is actually visible.
        compose.setContent {
            Surface(
                listening().copy(
                    peerName = "TOCHI-LAPTOP",
                    surfaceMode = SurfaceMode.MIRROR,
                    mirrorWidth = 1920,
                    mirrorHeight = 1080,
                    mirrorFrameReceived = true,
                ),
                mirrorControlsAutoHideMillis = null,
                initialMirrorControlsVisible = true,
                mirrorContent = { VideoStandIn() },
            )
        }

        compose.onNodeWithTag(ReceiverTags.MIRROR_CONTROLS_STATS).requestFocus()

        Snapshot.matches(compose, "receiver-mirror-control-focused")
    }

    @Test
    fun `the browser chrome while a page is loading`() {
        // Over white, deliberately. The chrome this replaced was muted grey text laid straight onto
        // the page, which was invisible on every light site — a defect only an image can show.
        compose.setContent {
            PageStandIn {
                ReceiverBrowserChrome(
                    page = BrowserSurfaceUi(
                        url = "https://example.com/",
                        title = "Example Domain",
                        progressPercent = 45,
                        isLoading = true,
                    ),
                    autoHideMillis = null,
                )
            }
        }

        Snapshot.matches(compose, "receiver-browser-loading")
    }

    @Test
    fun `the browser chrome once the page has settled`() {
        compose.setContent {
            PageStandIn {
                ReceiverBrowserChrome(
                    page = BrowserSurfaceUi(
                        url = "https://en.wikipedia.org/wiki/Fire_TV",
                        title = "Amazon Fire TV - Wikipedia",
                        progressPercent = 100,
                    ),
                    autoHideMillis = null,
                )
            }
        }

        Snapshot.matches(compose, "receiver-browser-ready")
    }

    @Test
    fun `the browser chrome explaining a blocked address`() {
        // The state where the page underneath has nothing on it, so the chrome is the entire
        // message. It has to read from a sofa and say what to do next.
        compose.setContent {
            PageStandIn {
                ReceiverBrowserChrome(
                    page = BrowserSurfaceUi(
                        url = "file:///etc/passwd",
                        title = "Blocked",
                        failure = "That address was blocked. Try a different one from the desktop.",
                    ),
                    autoHideMillis = null,
                )
            }
        }

        Snapshot.matches(compose, "receiver-browser-blocked")
    }

    /** A receiver that has come up and is advertising itself, with a fixed code and address. */
    private fun listening() = ReceiverUiState(
        networkState = ReceiverNetworkState.LISTENING,
        pairingCode = "272390",
        address = "192.168.1.51",
        port = 47_855,
        detail = "Ready to pair",
    )

    /**
     * The receiver surface, with every source of variation pinned.
     *
     * Auto-hiding overlays are the danger: an image captured while one is fading differs from the
     * same image captured a frame later, and a suite that flickers is one nobody trusts. Each test
     * either holds an overlay open or leaves it out entirely.
     */
    @Composable
    private fun Surface(
        state: ReceiverUiState,
        playbackHudAutoHideMillis: Long? = null,
        mirrorControlsAutoHideMillis: Long? = null,
        initialMirrorControlsVisible: Boolean = false,
        playerContent: @Composable () -> Unit = {},
        mirrorContent: @Composable () -> Unit = {},
    ) {
        Box(Modifier.fillMaxSize()) {
            ReceiverSurface(
                state = state,
                playerContent = playerContent,
                mirrorContent = mirrorContent,
                playbackHudAutoHideMillis = playbackHudAutoHideMillis,
                mirrorControlsAutoHideMillis = mirrorControlsAutoHideMillis,
                initialMirrorControlsVisible = initialMirrorControlsVisible,
            )
        }
    }

    /**
     * Stands in for a rendered web page: white, because that is what most of the web is and what
     * the chrome has to stay readable over.
     */
    @Composable
    private fun PageStandIn(chrome: @Composable () -> Unit) {
        Box(Modifier.fillMaxSize().background(Color.White)) { chrome() }
    }

    /**
     * Stands in for decoded video.
     *
     * A flat colour rather than a real frame: the surfaces under test are the overlays and the
     * layout around the picture, and a real decode would make every image depend on a codec.
     */
    @Composable
    private fun VideoStandIn() {
        Box(Modifier.fillMaxSize().background(Color(0xFF1B2430)))
    }
}
