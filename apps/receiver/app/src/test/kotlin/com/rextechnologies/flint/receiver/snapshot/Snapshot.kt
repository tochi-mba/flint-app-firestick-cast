package com.rextechnologies.flint.receiver.snapshot

import android.graphics.Bitmap
import android.graphics.Canvas
import androidx.activity.ComponentActivity
import androidx.compose.ui.test.junit4.AndroidComposeTestRule
import java.io.File

/**
 * Renders receiver UI and holds it to an approved image.
 *
 * The receiver has a harder testing problem than the Windows app: it runs on a television across
 * the room, driven by a remote control, and nobody sees it during development except as a
 * screenshot pulled off a device. Its layout also has constraints the desktop does not — overscan
 * margins, focus outlines that must be visible from two metres away, text large enough to read at
 * that distance. Those are exactly the properties that assertions cannot check and a rendered image
 * can.
 */
object Snapshot {
    /**
     * The frame size every receiver snapshot renders at.
     *
     * 1920x1080, because that is what the receiver actually runs at. Rendering the television UI at
     * a phone size would produce images that pass while the real layout on the panel is wrong,
     * which is the failure this project already hit once by checking the receiver on an emulator
     * instead of on the television.
     */
    const val WIDTH_DP: Int = 1920
    const val HEIGHT_DP: Int = 1080

    /** Whether this run should overwrite approved images instead of asserting against them. */
    val isUpdating: Boolean
        get() = System.getenv("FLINT_UPDATE_SNAPSHOTS").orEmpty() in setOf("1", "true") ||
            System.getProperty("flint.update.snapshots").orEmpty() in setOf("1", "true")

    /**
     * The instant every animated surface is sampled at.
     *
     * Any fixed value works; this one is a whole number of frames into the receiver's 950ms spinner
     * cycle, so the sampled frame is a recognisable position rather than an arbitrary smear.
     */
    const val ANIMATION_SAMPLE_MILLIS: Long = 475

    /** The directory holding approved images. */
    val approvedDirectory: File
        get() = File(snapshotRoot, "approved").also { it.mkdirs() }

    /**
     * The directory holding images from failed runs.
     *
     * Separate from the approved images and ignored by git, so a failing run cannot be turned into
     * an approval by an absent-minded `git add .`.
     */
    val rejectedDirectory: File
        get() = File(snapshotRoot, "rejected").also { it.mkdirs() }

    /**
     * Captures the composed tree and compares it with the approved image for [name].
     *
     * @throws AssertionError when the frame differs by more than [maximumDifferingPercent], or when
     *   no approved image exists yet.
     */
    fun matches(
        rule: AndroidComposeTestRule<*, out ComponentActivity>,
        name: String,
        maximumDifferingPercent: Double = ImageComparer.DEFAULT_MAXIMUM_DIFFERING_PERCENT,
    ) {
        require(name.isNotBlank()) { "a snapshot needs a name" }

        // Never `waitForIdle`. The receiver's idle screen runs an endless spinner, so the
        // composition is never idle and the wait times out after two seconds — which is how this
        // suite failed on its first run, eleven tests at once, reporting a condition rather than
        // an animation. The clock is driven by hand to a fixed instant instead, which terminates
        // and makes the captured frame identical every run: an animation sampled at whatever moment
        // the machine happened to reach is the other way a screenshot suite becomes a coin toss.
        //
        // Frame by frame, not in one jump. The test rule runs effects between frames, so a button that
        // takes focus when it is composed plays its focus animation out as it would on a television.
        // Advanced in a single step, the focus landed at the sampled instant with no time to animate.
        val start = rule.mainClock.currentTime
        while (rule.mainClock.currentTime - start < ANIMATION_SAMPLE_MILLIS) {
            rule.mainClock.advanceTimeByFrame()
        }

        compare(name, rule.captureDecorView(), maximumDifferingPercent)
    }

    /**
     * Draws the activity's view hierarchy into a bitmap.
     *
     * Compose's own `captureToImage` cannot be used here. It calls `forceRedraw`, which waits on the
     * frame clock — the very clock these tests hold still to stop the spinner — so the two
     * requirements are mutually exclusive and the capture times out. Drawing the decor view is
     * synchronous and depends on no clock at all, which is what makes a frozen animation and a real
     * capture possible at the same time.
     */
    private fun AndroidComposeTestRule<*, out ComponentActivity>.captureDecorView(): Bitmap {
        val view = activity.window.decorView
        check(view.width > 0 && view.height > 0) {
            "the decor view has no size (${view.width}x${view.height}); the activity never laid out"
        }

        val bitmap = Bitmap.createBitmap(view.width, view.height, Bitmap.Config.ARGB_8888)
        view.draw(Canvas(bitmap))
        return bitmap
    }

    /** Holds a captured frame to its approved image. */
    fun compare(name: String, rendered: Bitmap, maximumDifferingPercent: Double) {
        val approved = File(approvedDirectory, "$name.png")

        if (isUpdating) {
            rendered.writeTo(approved)
            return
        }

        if (!approved.exists()) {
            // Written where a person can look at it rather than approved automatically. A suite
            // that adopts whatever it first renders cannot fail on a first run, which is exactly
            // when a new screen is most likely to be wrong.
            val rejected = File(rejectedDirectory, "$name.actual.png")
            rendered.writeTo(rejected)
            throw AssertionError(
                "no approved image for '$name'. The rendered frame is at $rejected. " +
                    "Check it looks right, then re-run with FLINT_UPDATE_SNAPSHOTS=1 to approve it.",
            )
        }

        val expected = approved.readBitmap()
        if (expected.width != rendered.width || expected.height != rendered.height) {
            throw AssertionError(
                "'$name' rendered at ${rendered.width}x${rendered.height} but the approved image " +
                    "is ${expected.width}x${expected.height}. A size change is never incidental.",
            )
        }

        val comparison = ImageComparer.compare(expected, rendered)
        if (comparison.isWithin(maximumDifferingPercent)) {
            return
        }

        val rejected = File(rejectedDirectory, "$name.actual.png")
        val diff = File(rejectedDirectory, "$name.diff.png")
        rendered.writeTo(rejected)
        ImageComparer.buildDiff(expected, rendered).writeTo(diff)

        throw AssertionError(
            buildString {
                appendLine("'$name' does not match its approved image: $comparison, over the ")
                appendLine("${"%.3f".format(maximumDifferingPercent)}% budget.")
                appendLine("  approved: $approved")
                appendLine("  rendered: $rejected")
                appendLine("  diff:     $diff")
                append("If the change is intended, re-run with FLINT_UPDATE_SNAPSHOTS=1.")
            },
        )
    }

    /**
     * Where approved images live, resolved against the module rather than the build output.
     *
     * A test run has to be able to write a rejected image into the repository next to the one it
     * failed against, so the two can be looked at side by side. Anything under `build/` is wiped by
     * the next clean and is somewhere nobody thinks to look.
     */
    private val snapshotRoot: File
        get() {
            // Robolectric runs with the module directory as the working directory.
            val module = File(System.getProperty("user.dir") ?: ".")
            return File(module, "src/test/snapshots")
        }

    private fun Bitmap.writeTo(file: File) {
        file.parentFile?.mkdirs()
        file.outputStream().use { compress(Bitmap.CompressFormat.PNG, 100, it) }
    }

    private fun File.readBitmap(): Bitmap =
        android.graphics.BitmapFactory.decodeFile(absolutePath)
            ?: throw AssertionError("could not read the approved image at $this")
}
