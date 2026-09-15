package com.rextechnologies.flint.receiver.browser.workspace

/**
 * Pixel frames for workspace panes inside the mosaic preview/capture root.
 *
 * Kept pure so Windows preview hits and the TV layout use the same arithmetic.
 */
object BrowserWorkspaceLayoutFrames {
    data class Frame(val left: Int, val top: Int, val width: Int, val height: Int) {
        fun contains(x: Int, y: Int): Boolean =
            width > 0 && height > 0 && x in left until (left + width) && y in top until (top + height)

        fun localX(x: Int): Int = (x - left).coerceIn(0, (width - 1).coerceAtLeast(0))

        fun localY(y: Int): Int = (y - top).coerceIn(0, (height - 1).coerceAtLeast(0))
    }

    /**
     * @param gapPixels separator between panes (matches the native host gap).
     * @return one frame per visible slot, in slot order.
     */
    fun frames(
        layout: BrowserWorkspaceLayout,
        paneCount: Int,
        width: Int,
        height: Int,
        gapPixels: Int,
        split: BrowserWorkspaceSplit = BrowserWorkspaceSplit.Even,
    ): List<Frame> {
        if (width <= 0 || height <= 0 || paneCount <= 0) return emptyList()
        val count = paneCount.coerceIn(1, 4)
        val gap = gapPixels.coerceAtLeast(0)
        return when (layout) {
            BrowserWorkspaceLayout.SINGLE -> listOf(Frame(0, 0, width, height))
            BrowserWorkspaceLayout.SPLIT_HORIZONTAL -> split(width, height, count, horizontal = true, gap, split.column)
            BrowserWorkspaceLayout.SPLIT_VERTICAL -> split(width, height, count, horizontal = false, gap, split.row)
            BrowserWorkspaceLayout.GRID_2X2 -> grid(width, height, count, gap, split)
        }
    }

    /**
     * Resolves a mosaic-space point to a pane slot and local coordinates inside that pane.
     * Points in the gap between panes miss.
     */
    fun hitTest(
        layout: BrowserWorkspaceLayout,
        paneCount: Int,
        width: Int,
        height: Int,
        x: Int,
        y: Int,
        gapPixels: Int,
        split: BrowserWorkspaceSplit = BrowserWorkspaceSplit.Even,
    ): Hit? {
        val slots = frames(layout, paneCount, width, height, gapPixels, split)
        slots.forEachIndexed { index, frame ->
            if (frame.contains(x, y)) {
                return Hit(slot = index, localX = frame.localX(x), localY = frame.localY(y), frame = frame)
            }
        }
        return null
    }

    data class Hit(val slot: Int, val localX: Int, val localY: Int, val frame: Frame)

    private fun split(
        width: Int,
        height: Int,
        count: Int,
        horizontal: Boolean,
        gap: Int,
        fraction: BrowserWorkspaceSplitFraction,
    ): List<Frame> {
        if (count <= 1) return listOf(Frame(0, 0, width, height))
        return if (horizontal) {
            val columnGap = gap.coerceAtMost((width - 2).coerceAtLeast(0))
            val contentWidth = width - columnGap
            val firstWidth = extent(contentWidth, fraction)
            listOf(
                Frame(0, 0, firstWidth, height),
                Frame(firstWidth + columnGap, 0, contentWidth - firstWidth, height),
            )
        } else {
            val rowGap = gap.coerceAtMost((height - 2).coerceAtLeast(0))
            val contentHeight = height - rowGap
            val firstHeight = extent(contentHeight, fraction)
            listOf(
                Frame(0, 0, width, firstHeight),
                Frame(0, firstHeight + rowGap, width, contentHeight - firstHeight),
            )
        }
    }

    private fun extent(pixels: Int, fraction: BrowserWorkspaceSplitFraction): Int {
        if (pixels < 2) return pixels
        return (pixels.toLong() * fraction.tenThousandths / BrowserWorkspaceSplitFraction.SCALE)
            .toInt().coerceIn(1, pixels - 1)
    }

    private fun grid(width: Int, height: Int, count: Int, gap: Int, split: BrowserWorkspaceSplit): List<Frame> {
        if (count <= 1) return listOf(Frame(0, 0, width, height))
        val columnGap = gap.coerceAtMost((width - 2).coerceAtLeast(0))
        val contentWidth = width - columnGap
        val rowGap = gap.coerceAtMost((height - 2).coerceAtLeast(0))
        val contentHeight = height - rowGap
        val leftWidth = extent(contentWidth, split.column)
        val topHeight = extent(contentHeight, split.row)
        val all = listOf(
            Frame(0, 0, leftWidth, topHeight),
            Frame(leftWidth + columnGap, 0, contentWidth - leftWidth, topHeight),
            Frame(0, topHeight + rowGap, leftWidth, contentHeight - topHeight),
            Frame(leftWidth + columnGap, topHeight + rowGap, contentWidth - leftWidth, contentHeight - topHeight),
        )
        return all.take(count)
    }
}
