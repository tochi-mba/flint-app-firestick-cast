package com.rextechnologies.flint.receiver.browser

import kotlin.test.*

class BrowserHelpModelTest {
    @Test fun `help never opens automatically for new or returning users`() {
        assertFalse(BrowserHelpProgress().open)
        assertFalse(BrowserHelpProgress(dismissed = true).open)
    }

    @Test fun `closing keeps reading position without marking complete`() {
        val progress = BrowserHelpProgress().show().next().hide()
        assertEquals(1, progress.index)
        assertFalse(progress.dismissed)
        assertEquals(1, progress.show().index)
    }

    @Test fun `completing remains replayable and resets position`() {
        var progress = BrowserHelpProgress().show()
        repeat(BrowserHelpTopics.entries.size) { progress = progress.next() }
        assertFalse(progress.open)
        assertTrue(progress.dismissed)
        assertEquals(0, progress.show().index)
        assertTrue(progress.show().open)
    }

    @Test fun `previous cannot underflow`() {
        assertEquals(0, BrowserHelpProgress().back().index)
    }

    @Test fun `topics have usable nonempty headings and bounded instructions`() {
        assertEquals(BrowserHelpTopics.entries.size, BrowserHelpTopics.entries.map { it.title }.toSet().size)
        BrowserHelpTopics.entries.forEach {
            assertTrue(it.title.length in 1..50)
            assertTrue(it.body.length in 1..600)
        }
    }
}
