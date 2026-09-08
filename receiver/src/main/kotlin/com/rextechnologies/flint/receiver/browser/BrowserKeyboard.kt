package com.rextechnologies.flint.receiver.browser

/** Which set of keys is showing. */
enum class BrowserKeyboardPage { LETTERS, SYMBOLS }

/** One key. */
sealed interface BrowserKey {
    data class Character(val lower: Char, val upper: Char) : BrowserKey
    data object Space : BrowserKey
    data object Backspace : BrowserKey
    data object Shift : BrowserKey
    data object Clear : BrowserKey
    data object Submit : BrowserKey

    /** A whole suffix in one press. Saves four presses on the commonest ending there is. */
    data class Shortcut(val text: String) : BrowserKey

    data class SwitchPage(val page: BrowserKeyboardPage) : BrowserKey
}

/** Which key the D-pad is on. */
data class BrowserKeyCursor(val row: Int, val column: Int)

data class BrowserKeyboardState(
    val text: String = "",
    val page: BrowserKeyboardPage = BrowserKeyboardPage.LETTERS,
    val cursor: BrowserKeyCursor = BrowserKeyCursor(0, 0),
    val shifted: Boolean = false,
)

/**
 * The on-screen keyboard, as a model rather than a layout.
 *
 * Typing is the worst part of every television browser and the reason the address bar was left off
 * this one entirely. It cannot be avoided, so it is at least made predictable: a rectangular grid
 * with wrap-around in both axes so no edge is a dead end, a shift that releases after one letter,
 * and a `.com` key because that suffix is four presses people should not have to make.
 *
 * Pure, so the grid is proven complete and escape-proof by tests instead of by pressing every key on
 * a television.
 *
 * The desktop remains the faster way to type, and the sheet says so — but it is never the only way.
 */
class BrowserKeyboard {
    companion object {
        /** Matches the wire's address bound, so nothing typed here can fail to send. */
        const val MAX_LENGTH = 2_048
    }

    private val letterRows: List<List<BrowserKey>> = listOf(
        row("qwertyuiop"),
        row("asdfghjkl") + BrowserKey.Backspace,
        row("zxcvbnm") + listOf(BrowserKey.Character('-', '_'), BrowserKey.Character('.', '.'), BrowserKey.Shift),
        listOf(
            BrowserKey.SwitchPage(BrowserKeyboardPage.SYMBOLS),
            BrowserKey.Space,
            BrowserKey.Shortcut(".com"),
            BrowserKey.Character('/', '?'),
            BrowserKey.Character(':', ';'),
            BrowserKey.Clear,
            BrowserKey.Submit,
            BrowserKey.Character('_', '_'),
            BrowserKey.Character('~', '`'),
            BrowserKey.Character('%', '^'),
        ),
    )

    private val symbolRows: List<List<BrowserKey>> = listOf(
        row("1234567890"),
        listOf('!', '@', '#', '$', '&', '*', '(', ')', '\'', '"').map { BrowserKey.Character(it, it) },
        listOf('+', '=', '?', ',', '<', '>', '[', ']', '{', '}').map { BrowserKey.Character(it, it) },
        listOf(
            BrowserKey.SwitchPage(BrowserKeyboardPage.LETTERS),
            BrowserKey.Space,
            BrowserKey.Shortcut(".co.uk"),
            BrowserKey.Character('|', '\\'),
            BrowserKey.Character(';', ':'),
            BrowserKey.Clear,
            BrowserKey.Submit,
            BrowserKey.Backspace,
            BrowserKey.Character('^', '^'),
            BrowserKey.Character('`', '`'),
        ),
    )

    fun rows(page: BrowserKeyboardPage): List<List<BrowserKey>> = when (page) {
        BrowserKeyboardPage.LETTERS -> letterRows
        BrowserKeyboardPage.SYMBOLS -> symbolRows
    }

    fun keyAt(cursor: BrowserKeyCursor, page: BrowserKeyboardPage): BrowserKey {
        val grid = rows(page)
        val row = grid[cursor.row.coerceIn(grid.indices)]
        return row[cursor.column.coerceIn(row.indices)]
    }

    /**
     * Moves the selection, wrapping at every edge.
     *
     * Wrapping is not a nicety here. Without it the outer columns are dead ends, and getting from
     * the left of a row to Backspace on the right means walking the entire row.
     */
    fun move(
        cursor: BrowserKeyCursor,
        direction: CursorDirection,
        page: BrowserKeyboardPage,
    ): BrowserKeyCursor {
        val grid = rows(page)
        val rowCount = grid.size
        return when (direction) {
            CursorDirection.UP, CursorDirection.DOWN -> {
                val delta = if (direction == CursorDirection.DOWN) 1 else -1
                val row = ((cursor.row + delta) % rowCount + rowCount) % rowCount
                // Rows are equal width by construction, but clamping keeps this honest if that ever
                // stops being true.
                BrowserKeyCursor(row, cursor.column.coerceIn(grid[row].indices))
            }
            CursorDirection.LEFT, CursorDirection.RIGHT -> {
                val width = grid[cursor.row].size
                val delta = if (direction == CursorDirection.RIGHT) 1 else -1
                BrowserKeyCursor(cursor.row, ((cursor.column + delta) % width + width) % width)
            }
        }
    }

    fun press(state: BrowserKeyboardState, key: BrowserKey): BrowserKeyboardState = when (key) {
        is BrowserKey.Character -> append(state, (if (state.shifted) key.upper else key.lower).toString())
            // Shift releases after one letter. A sticky shift silently ruins everything typed after
            // it, which is far more expensive to notice and undo than pressing it twice.
            .copy(shifted = false)

        BrowserKey.Space -> append(state, " ")
        is BrowserKey.Shortcut -> append(state, key.text)
        BrowserKey.Backspace -> state.copy(text = state.text.dropLast(1))
        BrowserKey.Clear -> state.copy(text = "")
        BrowserKey.Shift -> state.copy(shifted = !state.shifted)
        BrowserKey.Submit -> state
        is BrowserKey.SwitchPage -> state.copy(
            page = key.page,
            // The cursor is placed rather than kept: the pages need not be the same shape, and
            // landing outside the new grid would be a press that does nothing.
            cursor = state.cursor.coerceInto(rows(key.page)),
            shifted = false,
        )
    }

    fun submit(state: BrowserKeyboardState): String = state.text.trim()

    /** What a key shows. Kept beside the model so the composable never invents a label. */
    fun label(key: BrowserKey, shifted: Boolean): String = when (key) {
        is BrowserKey.Character -> (if (shifted) key.upper else key.lower).toString()
        BrowserKey.Space -> "space"
        BrowserKey.Backspace -> "⌫"
        BrowserKey.Shift -> "⇧"
        BrowserKey.Clear -> "clear"
        BrowserKey.Submit -> "go"
        is BrowserKey.Shortcut -> key.text
        is BrowserKey.SwitchPage -> when (key.page) {
            BrowserKeyboardPage.SYMBOLS -> "?123"
            BrowserKeyboardPage.LETTERS -> "abc"
        }
    }

    private fun append(state: BrowserKeyboardState, text: String): BrowserKeyboardState {
        if (state.text.length >= MAX_LENGTH) {
            return state
        }
        val room = MAX_LENGTH - state.text.length
        return state.copy(text = state.text + text.take(room))
    }

    private fun row(characters: String): List<BrowserKey> =
        characters.map { BrowserKey.Character(it, it.uppercaseChar()) }

    private fun BrowserKeyCursor.coerceInto(grid: List<List<BrowserKey>>): BrowserKeyCursor {
        val safeRow = row.coerceIn(grid.indices)
        return BrowserKeyCursor(safeRow, column.coerceIn(grid[safeRow].indices))
    }
}
