package com.rextechnologies.flint.castcore.copy

import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class HonestyRulesTest {
    @Test
    fun `every word that makes a confidentiality claim is caught`() {
        HonestyRules.CONFIDENTIALITY_WORDS.forEach { word ->
            assertContains(
                HonestyRules.confidentialityClaims("The link is $word between the two."),
                word,
            )
        }
    }

    @Test
    fun `a claim is matched however it is capitalised`() {
        assertContains(HonestyRules.confidentialityClaims("Secure by default."), "secure")
        assertContains(HonestyRules.confidentialityClaims("ENCRYPTED at rest."), "encrypted")
    }

    @Test
    fun `a word that merely contains one is not a claim`() {
        assertTrue(HonestyRules.confidentialityClaims("This link is insecure.").isEmpty())
        assertTrue(HonestyRules.confidentialityClaims("A privateer sailed by.").isEmpty())
    }

    @Test
    fun `clean copy claims nothing`() {
        assertTrue(
            HonestyRules
                .confidentialityClaims("The television is reachable and the path is sufficient.")
                .isEmpty(),
        )
    }

    @Test
    fun `several claims in one sentence are all reported`() {
        val claims = HonestyRules.confidentialityClaims("A private, encrypted, secure link.")
        assertTrue(claims.containsAll(listOf("private", "encrypted", "secure")), claims.toString())
    }

    @Test
    fun `a sentence has to end like one`() {
        assertTrue(HonestyRules.isCompleteSentence("It works."))
        assertTrue(HonestyRules.isCompleteSentence("Does it? "))
        assertTrue(HonestyRules.isCompleteSentence("Waiting…"))
        assertTrue(HonestyRules.isCompleteSentence("What to do:"))
        assertFalse(HonestyRules.isCompleteSentence("a fragment"))
        assertFalse(HonestyRules.isCompleteSentence("   "))
        assertFalse(HonestyRules.isCompleteSentence(""))
    }

    @Test
    fun `the product does not raise its voice`() {
        assertTrue(HonestyRules.raisesItsVoice("Ready!"))
        assertFalse(HonestyRules.raisesItsVoice("Ready."))
    }

    @Test
    fun `the length bound is a number rather than a feeling`() {
        assertEquals(420, HonestyRules.MAXIMUM_COPY_LENGTH)
    }
}
