package com.rextechnologies.flint.mobile.ui

import androidx.compose.ui.text.AnnotatedString
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * The caret arithmetic behind the pairing code's grouping.
 *
 * A transformation that displays "123 456" for "123456" has to say where every caret position in
 * one maps to in the other, or editing the fourth digit lands the caret somewhere else. That mapping
 * is the part that goes wrong silently, so it is the part with a test.
 */
class GroupedDigitsTest {
    private fun transform(digits: String) = GroupedDigits.filter(AnnotatedString(digits))

    @Test
    fun `six digits display as two groups of three`() {
        assertEquals("123 456", transform("123456").text.text)
        assertEquals("123", transform("123").text.text)
        assertEquals("1234", transform("1234").text.text.replace(" ", ""))
        assertEquals("", transform("").text.text)
    }

    @Test
    fun `every original caret position maps into the display and back again`() {
        val mapping = transform("123456").offsetMapping
        (0..6).forEach { original ->
            val transformed = mapping.originalToTransformed(original)
            assertEquals(if (original <= 3) original else original + 1, transformed, "original $original")
            assertEquals(original, mapping.transformedToOriginal(transformed), "round trip of $original")
        }
    }

    @Test
    fun `a caret on the space maps to the digit after it, not past the end`() {
        val mapping = transform("1234").offsetMapping
        // Display is "123 4"; a caret at display offset 4 (after the space) is original offset 3.
        assertEquals(3, mapping.transformedToOriginal(4))
        assertEquals(4, mapping.transformedToOriginal(5))
        // Nothing past the digits that exist.
        assertEquals(4, mapping.transformedToOriginal(9))
    }

    @Test
    fun `a short code has no space and maps as the identity`() {
        val mapping = transform("12").offsetMapping
        (0..2).forEach { offset ->
            assertEquals(offset, mapping.originalToTransformed(offset))
            assertEquals(offset, mapping.transformedToOriginal(offset))
        }
    }
}
