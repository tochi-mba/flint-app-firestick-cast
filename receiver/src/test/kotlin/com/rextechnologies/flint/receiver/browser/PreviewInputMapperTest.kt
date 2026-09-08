package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserPointerAction
import com.rextechnologies.flint.protocol.wire.BrowserPointerInput
import com.rextechnologies.flint.protocol.wire.BrowserScrollInput
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

class PreviewInputMapperTest {
    private val mapper = PreviewInputMapper()

    @Test
    fun `pointer maps normalised coordinates into the current viewport`() {
        val mapped = mapper.mapPointer(
            BrowserPointerInput(BrowserPointerAction.DOWN, 3, 9, 65_535, 0, 1),
            currentNavigationId = 3,
            currentFrameId = 9,
            viewportWidth = 1920,
            viewportHeight = 1080,
        )

        val pointer = assertIs<PreviewMappedInput.Pointer>(mapped)
        assertEquals(1919, pointer.x)
        assertEquals(0, pointer.y)
        assertEquals(1, pointer.buttons)
    }

    @Test
    fun `stale frame references are rejected`() {
        val mapped = mapper.mapScroll(
            BrowserScrollInput(3, 8, 100, 100, 0, -120),
            currentNavigationId = 3,
            currentFrameId = 9,
            viewportWidth = 800,
            viewportHeight = 600,
        )

        assertEquals(PreviewInputRejection.STALE_REFERENCE, (mapped as PreviewMappedInput.Rejected).reason)
    }

    @Test
    fun `pointer accepts last published preview frame id`() {
        val mapped = mapper.mapPointer(
            BrowserPointerInput(BrowserPointerAction.DOWN, 3, 42, 32_768, 32_768, 1),
            currentNavigationId = 3,
            currentFrameId = 42,
            viewportWidth = 1920,
            viewportHeight = 1080,
        )

        assertIs<PreviewMappedInput.Pointer>(mapped)
    }

    @Test
    fun `pointer accepts navigation id as legacy frame id`() {
        val mapped = mapper.mapPointer(
            BrowserPointerInput(BrowserPointerAction.DOWN, 3, 3, 0, 0, 1),
            currentNavigationId = 3,
            currentFrameId = 42,
            viewportWidth = 800,
            viewportHeight = 600,
        )

        assertIs<PreviewMappedInput.Pointer>(mapped)
    }
}
