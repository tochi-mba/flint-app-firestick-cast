package com.rextechnologies.flint.receiver.browser.workspace

import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.receiver.browser.BrowserNativeInput
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class BrowserWorkspaceLayoutFramesTest {
    @Test
    fun `unequal split draws and targets identical boundaries`() {
        val split = BrowserWorkspaceSplit.of(7000, 3000)
        for (layout in BrowserWorkspaceLayout.entries) {
            val frames = BrowserWorkspaceLayoutFrames.frames(layout, 4, 204, 104, 4, split)
            frames.forEachIndexed { slot, frame ->
                val hit = BrowserWorkspaceLayoutFrames.hitTest(
                    layout,
                    4,
                    204,
                    104,
                    frame.left,
                    frame.top,
                    4,
                    split,
                )
                assertNotNull(hit)
                assertEquals(slot, hit.slot)
                assertEquals(0, hit.localX)
                assertEquals(0, hit.localY)
            }
        }
        val frames = BrowserWorkspaceLayoutFrames.frames(
            BrowserWorkspaceLayout.GRID_2X2,
            4,
            204,
            104,
            4,
            split,
        )
        assertEquals(BrowserWorkspaceLayoutFrames.Frame(0, 0, 140, 30), frames[0])
        assertEquals(BrowserWorkspaceLayoutFrames.Frame(144, 34, 60, 70), frames[3])
        assertNull(
            BrowserWorkspaceLayoutFrames.hitTest(
                BrowserWorkspaceLayout.GRID_2X2,
                4,
                204,
                104,
                141,
                10,
                4,
                split,
            ),
        )
    }

    @Test
    fun `tiny roots and oversized gaps never put panes outside the mosaic`() {
        for (width in 1..8) {
            for (height in 1..8) {
                for (layout in BrowserWorkspaceLayout.entries) {
                    for (split in listOf(BrowserWorkspaceSplit.of(1500, 8500), BrowserWorkspaceSplit.Even)) {
                        val frames = BrowserWorkspaceLayoutFrames.frames(layout, 4, width, height, 99, split)
                        frames.forEach { frame ->
                            assertTrue(frame.left >= 0 && frame.top >= 0)
                            assertTrue(frame.width >= 0 && frame.height >= 0)
                            assertTrue(frame.left + frame.width <= width)
                            assertTrue(frame.top + frame.height <= height)
                        }
                    }
                }
            }
        }
    }

    @Test
    fun `extreme nudges clamp without integer overflow`() {
        assertEquals(8500, BrowserWorkspaceSplitFraction.Even.nudged(Int.MAX_VALUE).tenThousandths)
        assertEquals(1500, BrowserWorkspaceSplitFraction.Even.nudged(Int.MIN_VALUE).tenThousandths)
    }

    @Test
    fun `side by side left click stays on slot zero`() {
        val hit = BrowserWorkspaceLayoutFrames.hitTest(
            layout = BrowserWorkspaceLayout.SPLIT_HORIZONTAL,
            paneCount = 2,
            width = 200,
            height = 100,
            x = 20,
            y = 40,
            gapPixels = 4,
        )
        assertNotNull(hit)
        assertEquals(0, hit.slot)
        assertEquals(20, hit.localX)
        assertEquals(40, hit.localY)
    }

    @Test
    fun `side by side right click maps into second pane local origin`() {
        // contentWidth = 196, firstWidth = 98, second starts at 102
        val hit = BrowserWorkspaceLayoutFrames.hitTest(
            layout = BrowserWorkspaceLayout.SPLIT_HORIZONTAL,
            paneCount = 2,
            width = 200,
            height = 100,
            x = 150,
            y = 10,
            gapPixels = 4,
        )
        assertNotNull(hit)
        assertEquals(1, hit.slot)
        assertEquals(48, hit.localX)
        assertEquals(10, hit.localY)
    }

    @Test
    fun `gap between panes is a miss so the wrong page is never clicked`() {
        val miss = BrowserWorkspaceLayoutFrames.hitTest(
            layout = BrowserWorkspaceLayout.SPLIT_HORIZONTAL,
            paneCount = 2,
            width = 200,
            height = 100,
            x = 99, // first pane ends at 98; gap is 98..101
            y = 50,
            gapPixels = 4,
        )
        assertNull(miss)
    }

    @Test
    fun `stacked bottom click targets lower pane`() {
        val hit = BrowserWorkspaceLayoutFrames.hitTest(
            layout = BrowserWorkspaceLayout.SPLIT_VERTICAL,
            paneCount = 2,
            width = 100,
            height = 200,
            x = 10,
            y = 150,
            gapPixels = 4,
        )
        assertNotNull(hit)
        assertEquals(1, hit.slot)
    }

    @Test
    fun `grid four corners resolve to distinct slots`() {
        val slots = listOf(
            10 to 10,
            150 to 10,
            10 to 150,
            150 to 150,
        ).map { (x, y) ->
            BrowserWorkspaceLayoutFrames.hitTest(
                BrowserWorkspaceLayout.GRID_2X2,
                paneCount = 4,
                width = 200,
                height = 200,
                x = x,
                y = y,
                gapPixels = 4,
            )!!.slot
        }
        assertEquals(listOf(0, 1, 2, 3), slots)
    }

    @Test
    fun `single pane fills the mosaic`() {
        val frames = BrowserWorkspaceLayoutFrames.frames(
            BrowserWorkspaceLayout.SINGLE,
            paneCount = 1,
            width = 640,
            height = 360,
            gapPixels = 8,
        )
        assertEquals(1, frames.size)
        assertEquals(BrowserWorkspaceLayoutFrames.Frame(0, 0, 640, 360), frames.single())
    }

    @Test
    fun `side by side first pane ends before the gap`() {
        val frames = BrowserWorkspaceLayoutFrames.frames(
            BrowserWorkspaceLayout.SPLIT_HORIZONTAL,
            paneCount = 2,
            width = 200,
            height = 100,
            gapPixels = 4,
        )
        assertEquals(2, frames.size)
        assertEquals(0, frames[0].left)
        assertEquals(98, frames[0].width)
        assertEquals(102, frames[1].left)
        assertNull(
            BrowserWorkspaceLayoutFrames.hitTest(
                BrowserWorkspaceLayout.SPLIT_HORIZONTAL,
                2,
                200,
                100,
                x = 99,
                y = 50,
                gapPixels = 4,
            ),
        )
    }
}
