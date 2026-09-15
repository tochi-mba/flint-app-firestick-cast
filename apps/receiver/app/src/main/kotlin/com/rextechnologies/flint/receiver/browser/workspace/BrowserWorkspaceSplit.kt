package com.rextechnologies.flint.receiver.browser.workspace

/**
 * Where the dividers sit in a mosaic, as fractions of the whole.
 *
 * The mosaic used to be four fixed arrangements with every pane an equal share. People want one
 * page bigger than another — a video beside a smaller feed, a reference page narrower than the
 * thing being read.
 *
 * Adjustable dividers rather than free rectangles, deliberately:
 *
 *  * A divider cannot produce an overlapping, gapped, or zero-area pane. Free rectangles can, and
 *    then every consumer — hit testing, capture, focus — has to decide what to do about it.
 *  * One number moves with Left and Right on a remote. A rectangle needs a pointer, and the
 *    television only reliably has a D-pad.
 *  * It survives a layout change. Switching from side-by-side to grid keeps the column the person
 *    chose, instead of discarding a layout they built.
 *
 * Stored as ten-thousandths so the value crosses the wire as an integer and halves are
 * exact. [MINIMUM] and [MAXIMUM] keep a pane from being dragged away to nothing.
 */
@JvmInline
value class BrowserWorkspaceSplitFraction private constructor(val tenThousandths: Int) {
    /** The fraction as a 0..1 multiplier, for laying out pixels. */
    val ratio: Float get() = tenThousandths / SCALE.toFloat()

    /** Moves the divider by [delta] ten-thousandths, staying inside the allowed range. */
    fun nudged(
        delta: Int,
    ): BrowserWorkspaceSplitFraction = of(
        (tenThousandths.toLong() + delta).coerceIn(MINIMUM.toLong(), MAXIMUM.toLong()).toInt(),
    )

    companion object {
        const val SCALE: Int = 10_000

        /** No pane may be squeezed below 15% of the axis; smaller is not a pane, it is a sliver. */
        const val MINIMUM: Int = 1_500
        const val MAXIMUM: Int = SCALE - MINIMUM

        val Even: BrowserWorkspaceSplitFraction = BrowserWorkspaceSplitFraction(SCALE / 2)

        /** Clamps into the allowed range. Out-of-range input is corrected, never rejected. */
        fun of(tenThousandths: Int): BrowserWorkspaceSplitFraction =
            BrowserWorkspaceSplitFraction(tenThousandths.coerceIn(MINIMUM, MAXIMUM))
    }
}

/**
 * The two dividers a mosaic can have.
 *
 * [column] splits left from right, [row] splits top from bottom. A layout uses whichever it needs:
 * side-by-side uses the column, stacked uses the row, the grid uses both, and a single page uses
 * neither but keeps them so switching back restores what the person set.
 */
data class BrowserWorkspaceSplit(
    val column: BrowserWorkspaceSplitFraction = BrowserWorkspaceSplitFraction.Even,
    val row: BrowserWorkspaceSplitFraction = BrowserWorkspaceSplitFraction.Even,
) {

    companion object {
        val Even: BrowserWorkspaceSplit = BrowserWorkspaceSplit()

        /** Rebuilds from wire values, clamping each. */
        fun of(column: Int, row: Int): BrowserWorkspaceSplit = BrowserWorkspaceSplit(
            column = BrowserWorkspaceSplitFraction.of(column),
            row = BrowserWorkspaceSplitFraction.of(row),
        )
    }
}
