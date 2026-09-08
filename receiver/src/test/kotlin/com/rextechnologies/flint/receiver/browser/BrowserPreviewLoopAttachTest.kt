package com.rextechnologies.flint.receiver.browser

import android.view.View
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import kotlin.test.Test
import kotlin.test.assertSame
import kotlin.test.assertTrue

@RunWith(RobolectricTestRunner::class)
@Config(manifest = Config.NONE, sdk = [28])
class BrowserPreviewLoopAttachTest {
    @Test
    fun `attach accepts any View so mosaic roots can be captured`() {
        val publisher = BrowserPreviewPublisher(send = { true })
        publisher.setEnabled(true)
        val loop = BrowserPreviewLoop(
            capture = BrowserPreviewCapture(publisher),
            epoch = { 1L },
            navigationId = { 1L },
        )
        val mosaic = View(RuntimeEnvironment.getApplication())
        loop.attach(mosaic)
        assertSame(mosaic, loop.attachedTarget())
        loop.setEnabled(true)
        assertTrue(loop.attachedTarget() === mosaic)
        loop.detach()
    }
}
