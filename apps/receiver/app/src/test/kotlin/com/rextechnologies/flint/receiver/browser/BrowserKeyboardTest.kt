package com.rextechnologies.flint.receiver.browser

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertTrue

/**
 * Typing with four buttons and an OK.
 *
 * This is the single worst part of every television browser, so the layout is a tested model rather
 * than a grid someone laid out by eye: every key must be reachable, no press may fall off the edge
 * into nothing, and the row someone is on must survive changing pages.
 */
class BrowserKeyboardTest {
    private val keyboard = BrowserKeyboard()

    @Test
    fun `every page is a full rectangle so no press lands on a hole`() {
        BrowserKeyboardPage.entries.forEach { page ->
            val rows = keyboard.rows(page)
            val width = rows.first().size
            assertTrue(rows.all { it.size == width }, "$page is ragged: ${rows.map { it.size }}")
            assertTrue(rows.isNotEmpty(), "$page has no rows")
        }
    }

    @Test
    fun `the letters page carries a full alphabet`() {
        val letters = keyboard.rows(BrowserKeyboardPage.LETTERS)
            .flatten()
            .mapNotNull { (it as? BrowserKey.Character)?.lower }
            .toSet()

        ('a'..'z').forEach { assertTrue(it in letters, "missing $it") }
    }

    @Test
    fun `the digits are all reachable without hunting through symbols`() {
        val digits = keyboard.rows(BrowserKeyboardPage.SYMBOLS)
            .flatten()
            .mapNotNull { (it as? BrowserKey.Character)?.lower }
            .toSet()

        ('0'..'9').forEach { assertTrue(it in digits, "missing $it") }
    }

    @Test
    fun `moving right from the last column wraps to the first`() {
        // Wrapping matters more here than anywhere: without it the edge columns are a dead end and
        // reaching backspace from 'a' means walking the whole row back.
        val rows = keyboard.rows(BrowserKeyboardPage.LETTERS)
        val lastColumn = rows.first().size - 1

        val moved = keyboard.move(BrowserKeyCursor(0, lastColumn), CursorDirection.RIGHT, BrowserKeyboardPage.LETTERS)

        assertEquals(BrowserKeyCursor(0, 0), moved)
    }

    @Test
    fun `moving down from the last row wraps to the first`() {
        val rows = keyboard.rows(BrowserKeyboardPage.LETTERS)
        val lastRow = rows.size - 1

        val moved = keyboard.move(BrowserKeyCursor(lastRow, 0), CursorDirection.DOWN, BrowserKeyboardPage.LETTERS)

        assertEquals(BrowserKeyCursor(0, 0), moved)
    }

    @Test
    fun `moving stays inside the grid from every cell in every direction`() {
        BrowserKeyboardPage.entries.forEach { page ->
            val rows = keyboard.rows(page)
            rows.indices.forEach { row ->
                rows[row].indices.forEach { column ->
                    CursorDirection.entries.forEach { direction ->
                        val moved = keyboard.move(BrowserKeyCursor(row, column), direction, page)
                        assertTrue(moved.row in rows.indices, "$page $direction escaped rows")
                        assertTrue(moved.column in rows[moved.row].indices, "$page $direction escaped columns")
                    }
                }
            }
        }
    }

    @Test
    fun `typing a letter appends it`() {
        val state = BrowserKeyboardState(text = "bbc")

        val next = keyboard.press(state, BrowserKey.Character('d', 'D'))

        assertEquals("bbcd", next.text)
    }

    @Test
    fun `shift capitalises exactly one letter and then lets go`() {
        // A sticky shift is worse than no shift: every following letter comes out wrong and the
        // viewer has to notice and undo it.
        var state = keyboard.press(BrowserKeyboardState(), BrowserKey.Shift)
        assertTrue(state.shifted)

        state = keyboard.press(state, BrowserKey.Character('a', 'A'))
        assertEquals("A", state.text)
        assertTrue(!state.shifted)

        state = keyboard.press(state, BrowserKey.Character('b', 'B'))
        assertEquals("Ab", state.text)
    }

    @Test
    fun `backspace removes one character and is harmless when empty`() {
        val typed = BrowserKeyboardState(text = "ab")

        assertEquals("a", keyboard.press(typed, BrowserKey.Backspace).text)
        assertEquals("", keyboard.press(BrowserKeyboardState(), BrowserKey.Backspace).text)
    }

    @Test
    fun `clear empties the field in one press`() {
        val typed = BrowserKeyboardState(text = "a long thing someone mistyped")

        assertEquals("", keyboard.press(typed, BrowserKey.Clear).text)
    }

    @Test
    fun `the shortcut key appends a whole suffix`() {
        val state = BrowserKeyboardState(text = "example")

        assertEquals("example.com", keyboard.press(state, BrowserKey.Shortcut(".com")).text)
    }

    @Test
    fun `switching pages keeps what has been typed`() {
        val state = BrowserKeyboardState(text = "half typed")

        val switched = keyboard.press(state, BrowserKey.SwitchPage(BrowserKeyboardPage.SYMBOLS))

        assertEquals("half typed", switched.text)
        assertEquals(BrowserKeyboardPage.SYMBOLS, switched.page)
    }

    @Test
    fun `switching pages puts the cursor somewhere that exists`() {
        val onLastRow = BrowserKeyboardState(
            page = BrowserKeyboardPage.LETTERS,
            cursor = BrowserKeyCursor(keyboard.rows(BrowserKeyboardPage.LETTERS).size - 1, 0),
        )

        val switched = keyboard.press(onLastRow, BrowserKey.SwitchPage(BrowserKeyboardPage.SYMBOLS))
        val rows = keyboard.rows(switched.page)

        assertTrue(switched.cursor.row in rows.indices)
        assertTrue(switched.cursor.column in rows[switched.cursor.row].indices)
    }

    @Test
    fun `text is bounded so a stuck key cannot grow it without limit`() {
        var state = BrowserKeyboardState(text = "x".repeat(BrowserKeyboard.MAX_LENGTH))

        state = keyboard.press(state, BrowserKey.Character('y', 'Y'))

        assertEquals(BrowserKeyboard.MAX_LENGTH, state.text.length)
    }

    @Test
    fun `every key can say what it looks like`() {
        BrowserKeyboardPage.entries.forEach { page ->
            keyboard.rows(page).flatten().forEach { key ->
                assertTrue(keyboard.label(key, shifted = false).isNotEmpty(), "$key has no label")
                assertNotNull(keyboard.label(key, shifted = true))
            }
        }
    }

    @Test
    fun `submitting hands back what was typed, trimmed`() {
        val state = BrowserKeyboardState(text = "  bbc news  ")

        assertEquals("bbc news", keyboard.submit(state))
    }
}
