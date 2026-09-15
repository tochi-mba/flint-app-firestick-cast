package com.rextechnologies.flint.receiver

import android.content.Context
import android.content.Intent
import android.graphics.Rect
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.UiObject2
import androidx.test.uiautomator.Until
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith

/** Device-level receiver UX checks. These intentionally run against the rendered TV surface. */
@RunWith(AndroidJUnit4::class)
class ReceiverE2ETest {
    private lateinit var context: Context
    private lateinit var device: UiDevice

    @Before
    fun setUp() {
        context = ApplicationProvider.getApplicationContext()
        device = UiDevice.getInstance(InstrumentationRegistry.getInstrumentation())
    }

    @After
    fun tearDown() {
        device.pressHome()
    }

    @Test
    fun receiverLaunchesWithRealBrandAndAnHonestLifecycleState() {
        context.startActivity(
            Intent(context, ReceiverActivity::class.java)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK),
        )

        assertNotNull(waitForText("FLINT  /  RECEIVER"))
        assertTrue(
            listOf("STARTING FLINT", "SAME NETWORK REQUIRED", "READY FOR FLINT")
                .any { device.findObject(By.text(it)) != null },
        )
    }

    @Test
    fun readySurfaceShowsReadableAtomicCodeAddressAndInstructions() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_READY)

        assertNotNull(waitForText("READY FOR FLINT"))
        assertNotNull(device.findObject(By.text("PAIRING CODE")))
        assertNotNull(device.findObject(By.desc("Pairing code 0 0 4 2 8 3")))
        assertNotNull(device.findObject(By.text("10.221.159.172:47855")))
        assertNotNull(device.findObject(By.text("BROWSER PORT")))
        assertNotNull(device.findObject(By.desc("Browser port 33164")))
        assertNotNull(device.findObject(By.text("Open Flint on your Windows PC")))
        assertNotNull(device.findObject(By.text("Type the six-digit code")))
        assertNotNull(device.findObject(By.text("BROWSE ON THIS TV")))
        assertNotNull(device.findObject(By.desc("Receiver status: READY TO PAIR")))
    }

    @Test
    fun widestCodeAndEndpointRemainInsideTheFivePercentSafeArea() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_READY_WIDE)

        val nodes = listOf(
            requireObject(By.text("FLINT  /  RECEIVER")),
            requireObject(By.text("READY FOR FLINT")),
            requireObject(By.desc("Pairing code 8 8 8 8 8 8")),
            requireObject(By.text("255.255.255.255:65535")),
            requireObject(By.text("BROWSE ON THIS TV")),
            requireObject(By.text("NEW CODE")),
        )
        nodes.forEach(::assertInsideSafeArea)
        val codeBounds = nodes[2].visibleBounds
        assertTrue("Pairing code must remain a single horizontal row: $codeBounds", codeBounds.width() > codeBounds.height())
    }

    @Test
    fun dpadCenterRefreshesTheCodeAndKeepsTheActionFocused() {
        // Asserted through behaviour rather than the `focused` accessibility flag: Compose drives
        // focus inside a single host view, and this Fire OS build does not surface the flag on the
        // merged node even while focus is visibly and functionally correct. Browse is focused by
        // default on the ready surface, so move to NEW CODE before Select.
        launchPreview(ReceiverPreviewActivity.PREVIEW_READY)
        assertNotNull(waitForText("NEW CODE"))
        val before = requireObject(By.desc("Pairing code 0 0 4 2 8 3")).contentDescription

        device.pressDPadRight()
        device.waitForIdle()
        device.pressDPadCenter()
        assertNotNull(device.wait(Until.findObject(By.desc("Pairing code 8 8 8 8 8 8")), TIMEOUT_MILLIS))
        val after = requireObject(By.desc("Pairing code 8 8 8 8 8 8")).contentDescription
        assertNotEquals(before, after)
    }

    @Test
    fun offlineAndConnectedStatesDoNotOfferAnInvalidPairingAction() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_NO_NETWORK)
        assertNotNull(waitForText("SAME NETWORK REQUIRED"))
        assertNotNull(device.findObject(By.text("Connect this Fire TV")))
        assertEquals(null, device.findObject(By.text("NEW CODE")))

        launchPreview(ReceiverPreviewActivity.PREVIEW_CONNECTED)
        assertNotNull(waitForText("PC CONNECTED"))
        assertNotNull(device.findObject(By.text("CONNECTED")))
        assertNotNull(device.findObject(By.text("BROWSE ON THIS TV")))
        assertEquals(null, device.findObject(By.text("NEW CODE")))
    }

    @Test
    fun attentionStateFocusesRetryAndDpadCenterRecovers() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_ATTENTION)
        assertNotNull(device.findObject(By.desc(RECEIVER_START_FAILURE_MESSAGE)))
        assertNotNull(waitForText("TRY AGAIN"))

        device.pressDPadCenter()

        assertNotNull(waitForText("READY FOR FLINT"))
        assertNotNull(device.findObject(By.desc("Pairing code 0 0 4 2 8 3")))
    }

    @Test
    fun playbackLoadingPlayingAndPausedStatesUseLivingRoomCopy() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_BUFFERING)
        assertNotNull(waitForText("GETTING IT READY"))
        assertNotNull(device.findObject(By.textContains("Connecting to your PC")))
        assertNotNull(device.findObject(By.text("New Adobe 2026 VPro blade3 1160x870")))

        launchPreview(ReceiverPreviewActivity.PREVIEW_PLAYING)
        assertNotNull(waitForText("PLAYING"))
        assertNotNull(device.findObject(By.text("1:02:34  /  2:05:08")))
        assertNotNull(device.findObject(By.text("PAUSE")))
        assertNotNull(device.findObject(By.text("CLOSE")))

        launchPreview(ReceiverPreviewActivity.PREVIEW_PAUSED)
        assertNotNull(waitForText("PAUSED"))
        assertNotNull(device.findObject(By.text("1:01  /  2:05")))
        assertNotNull(device.findObject(By.text("PLAY")))
        assertNotNull(device.findObject(By.text("CLOSE")))
    }

    @Test
    fun playPauseButtonIsFocusedByDefaultAndTogglesWithDpadCenter() {
        // Pins the regression where the play/pause "button" was static hint text: nothing was
        // focusable, so the remote's SELECT key had no visible target and D-pad navigation between
        // "options" on this row did nothing at all.
        launchPreview(ReceiverPreviewActivity.PREVIEW_PLAYING)
        assertNotNull(waitForText("PAUSE"))

        device.pressDPadCenter()

        assertNotNull(device.wait(Until.findObject(By.text("PLAY")), TIMEOUT_MILLIS))
        assertNotNull(waitForText("PAUSED"))
    }

    @Test
    fun closeButtonIsReachableByDpadAndDpadCenterActivatesIt() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_PLAYING)
        assertNotNull(waitForText("PAUSE"))
        assertNotNull(device.findObject(By.text("CLOSE")))

        device.pressDPadRight()
        device.pressDPadCenter()

        assertNotNull(waitForText("PC CONNECTED"))
    }

    @Test
    fun playbackErrorIsActionableAndDoesNotExposeImplementationJargon() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_PLAYBACK_ERROR)

        val announcement = requireObject(By.descContains("PLAYBACK STOPPED"))
        val description = announcement.contentDescription.orEmpty()
        assertTrue(description.contains("Keep Flint open"))
        assertTrue(description.contains("Press BACK to close playback"))
        assertFalse(description.contains("ERROR_CODE"))
        assertFalse(description.contains("Media3"))
    }

    @Test
    fun mirrorHudDisappearsOnlyAfterTheFirstRenderedFrameAndUsesItsOwnError() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_MIRROR_WAITING)
        assertNotNull(waitForText("SCREEN MIRROR"))
        assertNotNull(device.findObject(By.text("Laptop display")))

        launchPreview(ReceiverPreviewActivity.PREVIEW_MIRROR_ACTIVE)
        assertFalse(device.hasObject(By.text("SCREEN MIRROR")))
        assertNotNull(device.findObject(By.text("2560 x 1600  TEST FRAME")))
        val frame = requireObject(By.desc("Preview frame 2560 by 1600")).visibleBounds
        val ratio = frame.width().toFloat() / frame.height()
        assertTrue("Expected a fitted 16:10 frame, got $frame", ratio in 1.58f..1.62f)
        assertTrue("Expected centered pillar bars, got $frame", kotlin.math.abs(frame.centerX() - device.displayWidth / 2) <= 2)
        assertTrue("Expected visible pillar bars, got $frame", frame.left > 0 && frame.right < device.displayWidth)

        launchPreview(ReceiverPreviewActivity.PREVIEW_MIRROR_ERROR)
        val announcement = requireObject(By.descContains("SCREEN MIRROR STOPPED"))
        val description = announcement.contentDescription.orEmpty()
        assertTrue(description.contains("Press BACK to close this screen"))
        assertFalse(description.contains("PLAYBACK STOPPED"))
    }

    @Test
    fun presentationHasDistinctWaitingAndActiveStates() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_PRESENTATION)
        assertNotNull(waitForText("SECOND SCREEN"))
        assertNotNull(device.findObject(By.text("Extended desktop")))

        launchPreview(ReceiverPreviewActivity.PREVIEW_PRESENTATION_ACTIVE)
        assertNotNull(device.findObject(By.text("1920 x 1080  TEST FRAME")))
        assertFalse(device.hasObject(By.text("SECOND SCREEN")))
    }

    @Test
    fun readySurfaceOffersStandaloneBrowseOnThisTv() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_READY)
        assertNotNull(waitForText("BROWSE ON THIS TV"))
    }

    @Test
    fun connectedSurfaceStillOffersStandaloneBrowse() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_CONNECTED)
        assertNotNull(waitForText("PC CONNECTED"))
        assertNotNull(waitForText("BROWSE ON THIS TV"))
    }

    @Test
    fun browseOnThisTvOpensTheBrowserHarnessFromReady() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_READY)
        device.pressDPadCenter()
        assertNotNull(waitForDesc("Harness page title"))
    }

    @Test
    fun offlineSurfaceKeepsBrowseDisabledCopyHonest() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_NO_NETWORK)
        assertNotNull(waitForText("SAME NETWORK REQUIRED"))
        assertEquals(null, device.findObject(By.text("BROWSE ON THIS TV")))
    }

    @Test
    fun bufferingTitleIsReadableWithoutFileExtensionNoise() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_BUFFERING)
        assertNotNull(waitForText("GETTING IT READY"))
        assertNotNull(device.findObject(By.text("New Adobe 2026 VPro blade3 1160x870")))
        assertFalse(device.hasObject(By.textContains(".mp4")))
    }

    @Test
    fun playingHudExposesTimelineAndLivingRoomActions() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_PLAYING)
        assertNotNull(waitForText("PLAYING"))
        assertNotNull(device.findObject(By.text("PAUSE")))
        assertNotNull(device.findObject(By.text("CLOSE")))
        assertNotNull(device.findObject(By.text("1:02:34  /  2:05:08")))
    }

    @Test
    fun mirrorWaitingCopyNamesTheLaptopAndAsksForPatience() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_MIRROR_WAITING)
        assertNotNull(waitForText("SCREEN MIRROR"))
        assertNotNull(device.findObject(By.text("Laptop display")))
    }

    @Test
    fun mirrorActiveFrameKeepsSourceAspectRatio() {
        // The preview frame is intentionally full-bleed (edge-to-edge). Overscan-safe margins apply
        // to chrome copy, not to the mirrored picture itself.
        launchPreview(ReceiverPreviewActivity.PREVIEW_MIRROR_ACTIVE)
        val frame = requireObject(By.desc("Preview frame 2560 by 1600"))
        val ratio = frame.visibleBounds.width().toFloat() / frame.visibleBounds.height()
        assertTrue("Expected a fitted 16:10 frame, got ${frame.visibleBounds}", ratio in 1.58f..1.62f)
        assertTrue(
            "Expected centered pillar bars, got ${frame.visibleBounds}",
            kotlin.math.abs(frame.visibleBounds.centerX() - device.displayWidth / 2) <= 2,
        )
    }

    @Test
    fun readyWidePairingCodeStaysASingleReadableRow() {
        launchPreview(ReceiverPreviewActivity.PREVIEW_READY_WIDE)
        val code = requireObject(By.desc("Pairing code 8 8 8 8 8 8"))
        assertInsideSafeArea(code)
        assertTrue(code.visibleBounds.width() > code.visibleBounds.height())
    }

    private fun launchPreview(state: String) {
        context.startActivity(
            Intent(context, ReceiverPreviewActivity::class.java)
                .putExtra(ReceiverPreviewActivity.EXTRA_STATE, state)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK),
        )
        assertNotNull(device.wait(Until.findObject(By.desc("Receiver preview $state")), TIMEOUT_MILLIS))
        device.waitForIdle()
    }

    private fun waitForText(text: String): UiObject2? =
        device.wait(Until.findObject(By.text(text)), TIMEOUT_MILLIS)

    private fun waitForDesc(desc: String): UiObject2? =
        device.wait(Until.findObject(By.desc(desc)), TIMEOUT_MILLIS)

    private fun requireObject(selector: androidx.test.uiautomator.BySelector): UiObject2 =
        requireNotNull(device.findObject(selector)) { "Missing UI object for $selector" }

    private fun assertInsideSafeArea(node: UiObject2) {
        val safeX = (device.displayWidth * 0.05f).toInt()
        val safeY = (device.displayHeight * 0.05f).toInt()
        val safeBounds = Rect(safeX, safeY, device.displayWidth - safeX, device.displayHeight - safeY)
        val visible = node.visibleBounds
        assertTrue("$visible escaped the 5% TV safe area $safeBounds", safeBounds.contains(visible))
    }

    private companion object {
        const val TIMEOUT_MILLIS = 15_000L
    }
}
