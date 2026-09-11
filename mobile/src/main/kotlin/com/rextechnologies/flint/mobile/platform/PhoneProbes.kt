package com.rextechnologies.flint.mobile.platform

import android.app.Activity
import android.app.Presentation
import android.content.Context
import android.graphics.PixelFormat
import android.hardware.display.DisplayManager
import android.media.Image
import android.media.ImageReader
import android.media.MediaCodecInfo
import android.media.MediaCodecList
import android.media.MediaFormat
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.DisplayMetrics
import android.view.Display
import android.view.View
import com.rextechnologies.flint.castcore.capability.PhoneCapabilities
import com.rextechnologies.flint.castcore.capability.ProbeOutcome
import com.rextechnologies.flint.protocol.wire.CodecId
import kotlin.math.abs
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Asks this phone what it can do, rather than inferring it from the model name or the API level.
 *
 * An API level says a method exists. It does not say that this vendor's implementation of it works,
 * and this project has already shipped one encoder that satisfied every structural check while
 * emitting frames that decoded to nothing at all. So every capability the UI reports is the result of
 * having asked, and until something has asked, the answer is [ProbeOutcome.NOT_PROBED] rather than a
 * guess in either direction.
 */
object PhoneProbes {
    /**
     * Encoders the platform reports as hardware-backed.
     *
     * Software encoders are excluded rather than ranked. Encoding 1080p in software cannot meet the
     * latency these modes exist for, and offering a mode that technically starts and then stutters is
     * worse than saying the phone cannot do it.
     */
    fun probeHardwareEncoders(): Set<CodecId> {
        val found = mutableSetOf<CodecId>()
        val list = MediaCodecList(MediaCodecList.REGULAR_CODECS)
        for (info in list.codecInfos) {
            if (!info.isEncoder) continue
            if (isSoftwareOnly(info)) continue
            for (type in info.supportedTypes) {
                when {
                    type.equals(MediaFormat.MIMETYPE_VIDEO_AVC, ignoreCase = true) ->
                        found += CodecId.H264

                    type.equals(MediaFormat.MIMETYPE_VIDEO_HEVC, ignoreCase = true) ->
                        found += CodecId.H265
                }
            }
        }
        return found
    }

    /**
     * Whether this codec runs on the CPU.
     *
     * `isSoftwareOnly` arrived in API 29. Below that the only signal is the naming convention every
     * Android build has followed since the platform codecs were written — `OMX.google.` for the old
     * stack and `c2.android.` for Codec2 — which is a convention rather than a contract, so it is
     * used only where the API that would answer properly does not exist.
     */
    private fun isSoftwareOnly(info: MediaCodecInfo): Boolean =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            info.isSoftwareOnly
        } else {
            val name = info.name.lowercase()
            name.startsWith("omx.google.") || name.startsWith("c2.android.")
        }

    /** Whether there is a screen-capture consent flow to ask for at all. */
    fun screenCaptureConsentAvailable(context: Context): Boolean =
        context.getSystemService(MediaProjectionManager::class.java) != null

    /** Playback capture arrived in API 29, and even there it only captures apps that allow it. */
    fun audioPlaybackCaptureSupported(): Boolean =
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q

    /**
     * The second-screen check, and the load-bearing one.
     *
     * The whole design rests on a claim about the platform: a `VirtualDisplay` this app owns and
     * renders into needs no permission, because `VIRTUAL_DISPLAY_FLAG_PUBLIC` is what pulls in
     * `CAPTURE_VIDEO_OUTPUT` and a private display does not set it. That is what the documentation
     * says. It is not what every vendor's build necessarily does, and a claim this much rests on is
     * worth checking rather than asserting.
     *
     * So this creates the display, puts a [Presentation] on it, paints one frame a known colour and
     * reads the pixel back. Nothing appears on any television, nothing is sent anywhere, and the
     * display is released whatever happens.
     *
     * It must be called from an Activity: a [Presentation] is a Dialog, and a Dialog needs an
     * Activity context unless it is given an overlay window type, which would need a permission this
     * app has no business asking for.
     */
    suspend fun probeVirtualDisplay(activity: Activity): ProbeOutcome = try {
        runVirtualDisplayProbe(activity)
    } catch (_: Throwable) {
        // Deliberately broad. This is a capability question, and every way of answering "no" —
        // a SecurityException, a vendor NPE inside DisplayManager, a timeout — is the same answer.
        ProbeOutcome.UNSUPPORTED
    }

    /**
     * Runs on the main thread, and suspends there rather than blocking.
     *
     * The distinction is the whole probe. A [Presentation] is a Dialog: it is created, shown and
     * drawn on the main looper, and its first frame is produced by a message posted to that same
     * looper. An earlier version of this polled for the frame with `Thread.sleep` on the main
     * thread, which meant the message that would have drawn it could not run until the polling gave
     * up — so the probe answered `UNSUPPORTED` on every phone ever made, after freezing the UI for
     * two seconds to do it. Suspending frees the looper to draw.
     */
    private suspend fun runVirtualDisplayProbe(activity: Activity): ProbeOutcome =
        withContext(Dispatchers.Main.immediate) {
            val displayManager = activity.getSystemService(DisplayManager::class.java)
                ?: return@withContext ProbeOutcome.UNSUPPORTED

            val reader = ImageReader.newInstance(PROBE_WIDTH, PROBE_HEIGHT, PixelFormat.RGBA_8888, 2)
            val painted = CompletableDeferred<Boolean>()
            reader.setOnImageAvailableListener(
                { source -> onProbeImage(source, painted) },
                Handler(Looper.getMainLooper()),
            )

            var display: android.hardware.display.VirtualDisplay? = null
            var presentation: Presentation? = null
            try {
                display = displayManager.createVirtualDisplay(
                    PROBE_DISPLAY_NAME,
                    PROBE_WIDTH,
                    PROBE_HEIGHT,
                    DisplayMetrics.DENSITY_DEFAULT,
                    reader.surface,
                    // No flags at all. PUBLIC is the one that would need a system permission, and
                    // PRESENTATION only affects whether the display is offered to other apps.
                    0,
                ) ?: return@withContext ProbeOutcome.UNSUPPORTED

                presentation = showProbeFrame(activity, display.display)
                    ?: return@withContext ProbeOutcome.UNSUPPORTED

                val arrived = withTimeoutOrNull(PROBE_TIMEOUT_MILLIS) { painted.await() } ?: false
                if (arrived) ProbeOutcome.SUPPORTED else ProbeOutcome.UNSUPPORTED
            } finally {
                runCatching { reader.setOnImageAvailableListener(null, null) }
                runCatching { presentation?.dismiss() }
                runCatching { display?.release() }
                runCatching { reader.close() }
            }
        }

    /**
     * Looks at one delivered frame, and answers only when it is the one that was painted.
     *
     * A virtual display commonly produces a blank frame before its content has drawn, so the first
     * image to arrive is not evidence either way. Completing only on a match, and letting the
     * timeout supply the negative, means a slow first frame reads as slow rather than as absent.
     */
    private fun onProbeImage(source: ImageReader, painted: CompletableDeferred<Boolean>) {
        while (!painted.isCompleted) {
            val image: Image = source.acquireLatestImage() ?: return
            try {
                if (probeColourArrived(image)) painted.complete(true)
            } finally {
                image.close()
            }
        }
    }

    private fun showProbeFrame(activity: Activity, display: Display): Presentation? {
        val presentation = Presentation(activity, display)
        val view = View(presentation.context)
        view.setBackgroundColor(PROBE_COLOUR)
        presentation.setContentView(view)
        return try {
            presentation.show()
            presentation
        } catch (_: Throwable) {
            runCatching { presentation.dismiss() }
            null
        }
    }

    /**
     * Whether this image is the colour that was painted.
     *
     * Checking the pixel rather than merely that an image arrived is the point. A display that
     * produces correctly-shaped buffers full of nothing is exactly the failure this project has
     * already shipped once, and it passed every structural check on the way out.
     */
    private fun probeColourArrived(image: Image): Boolean {
        val plane = image.planes.firstOrNull() ?: return false
        val buffer = plane.buffer
        val bytes = ByteArray(buffer.remaining())
        buffer.duplicate().get(bytes)
        val pixel = channelsAt(
            bytes = bytes,
            position = 0,
            rowStride = plane.rowStride,
            pixelStride = plane.pixelStride,
        ) ?: return false
        return pixel.matches(PROBE_RED, PROBE_GREEN, PROBE_BLUE)
    }

    /** One pixel's colour channels, in the order an RGBA_8888 plane stores them. */
    internal data class Rgb(val red: Int, val green: Int, val blue: Int) {
        /**
         * Compared with a small tolerance rather than exactly.
         *
         * RGBA_8888 is not supposed to alter what was written, but the path from a View's background
         * to a VirtualDisplay's buffer goes through a compositor that some vendors let apply
         * dithering or a colour transform. A channel two values out is still evidently the painted
         * colour; black is not, which is the case that has to fail.
         */
        fun matches(red: Int, green: Int, blue: Int): Boolean =
            abs(this.red - red) <= CHANNEL_TOLERANCE &&
                abs(this.green - green) <= CHANNEL_TOLERANCE &&
                abs(this.blue - blue) <= CHANNEL_TOLERANCE
    }

    /**
     * The colour of one pixel, or `null` when the buffer does not reach it.
     *
     * Pure, and separated from the probe for that reason: it is the part with arithmetic in it and
     * the part a unit test can reach without a display. It reads through the plane's own strides
     * rather than assuming the buffer is packed. It is not: a `rowStride` larger than
     * `width * pixelStride` is the ordinary case on hardware that aligns each row, and a
     * `pixelStride` above four is legal. An earlier version indexed bytes 0, 1 and 2 absolutely,
     * which also ignored the buffer's own position.
     */
    internal fun channelsAt(
        bytes: ByteArray,
        position: Int,
        rowStride: Int,
        pixelStride: Int,
        column: Int = 0,
        row: Int = 0,
    ): Rgb? {
        if (position < 0 || rowStride <= 0 || pixelStride <= 0 || column < 0 || row < 0) return null
        val offset = position.toLong() + row.toLong() * rowStride + column.toLong() * pixelStride
        if (offset < 0 || offset + 3 > bytes.size) return null
        val at = offset.toInt()
        return Rgb(
            red = bytes[at].toInt() and 0xff,
            green = bytes[at + 1].toInt() and 0xff,
            blue = bytes[at + 2].toInt() and 0xff,
        )
    }

    /** Everything the capability assessor needs about this phone, in one call. */
    fun capabilities(
        context: Context,
        deviceName: String,
        screenWidth: Int,
        screenHeight: Int,
        densityDpi: Int,
        encoders: Set<CodecId>,
        encoderProbe: ProbeOutcome,
        virtualDisplayProbe: ProbeOutcome,
    ): PhoneCapabilities = PhoneCapabilities(
        apiLevel = Build.VERSION.SDK_INT,
        deviceName = deviceName,
        screenWidth = screenWidth,
        screenHeight = screenHeight,
        densityDpi = densityDpi,
        hardwareVideoEncoders = encoders,
        encoderProbe = encoderProbe,
        virtualDisplayProbe = virtualDisplayProbe,
        screenCaptureConsentAvailable = screenCaptureConsentAvailable(context),
        audioPlaybackCaptureSupported = audioPlaybackCaptureSupported(),
    )

    private const val PROBE_DISPLAY_NAME = "flint-second-screen-check"
    private const val PROBE_WIDTH = 64
    private const val PROBE_HEIGHT = 64
    private const val PROBE_TIMEOUT_MILLIS = 2_000L

    /** How far a channel may drift from the colour that was painted and still count as it. */
    internal const val CHANNEL_TOLERANCE = 2

    // An arbitrary colour that no default background happens to be, so a frame of nothing cannot
    // pass by accident.
    private const val PROBE_RED = 0xD7
    private const val PROBE_GREEN = 0xFF
    private const val PROBE_BLUE = 0x3F
    private const val PROBE_COLOUR = 0xFFD7FF3F.toInt()
}
