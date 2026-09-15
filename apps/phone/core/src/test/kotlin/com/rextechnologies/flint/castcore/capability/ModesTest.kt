package com.rextechnologies.flint.castcore.capability

import kotlin.test.Test
import kotlin.test.assertContains
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertSame
import kotlin.test.assertTrue

class ModesTest {
    @Test
    fun `every cast mode gets a plain English title rather than its enum name`() {
        assertEquals("Mirror this screen", ModePresentation.titleOf(CastMode.MIRROR))
        assertEquals("Second screen", ModePresentation.titleOf(CastMode.SECOND_SCREEN))
        assertEquals("Play a file on the TV", ModePresentation.titleOf(CastMode.MEDIA_HANDOFF))
        assertEquals(3, CastMode.entries.size)
        val titles = CastMode.entries.map { ModePresentation.titleOf(it) }
        assertEquals(titles.size, titles.toSet().size)
        // No title may leak the enum spelling: SECOND_SCREEN is a constant name, not a sentence.
        assertTrue(titles.none { title -> CastMode.entries.any { it.name == title } })
        assertTrue(titles.none { it.contains('_') })
    }

    @Test
    fun `every mode status gets its own word and blocked never reads as not possible`() {
        assertEquals("Ready", StatusWord.of(ModeStatus.AVAILABLE))
        assertEquals("Blocked", StatusWord.of(ModeStatus.BLOCKED))
        assertEquals("Not possible", StatusWord.of(ModeStatus.IMPOSSIBLE))
        assertEquals("Coming soon", StatusWord.of(ModeStatus.NOT_IMPLEMENTED))
        assertEquals("Ready", StatusWord.AVAILABLE)
        assertEquals("Blocked", StatusWord.BLOCKED)
        assertEquals("Not possible", StatusWord.IMPOSSIBLE)
        assertEquals("Coming soon", StatusWord.NOT_IMPLEMENTED)
        assertEquals(4, ModeStatus.entries.size)
        // "Blocked" asks the reader to go and do something; "Not possible" asks them to stop trying.
        // One word for both outcomes would make those two instructions indistinguishable.
        val words = ModeStatus.entries.map { StatusWord.of(it) }
        assertEquals(words.size, words.toSet().size)
    }

    @Test
    fun `only an available verdict is offerable`() {
        assertTrue(verdict(status = ModeStatus.AVAILABLE).isOfferable)
        assertFalse(verdict(status = ModeStatus.BLOCKED, remedy = "Turn the hotspot on.").isOfferable)
        assertFalse(verdict(status = ModeStatus.IMPOSSIBLE).isOfferable)
        assertFalse(verdict(status = ModeStatus.NOT_IMPLEMENTED).isOfferable)
        val offerable = ModeStatus.entries.filter { verdict(status = it).isOfferable }
        assertEquals(listOf(ModeStatus.AVAILABLE), offerable)
    }

    @Test
    fun `a verdict with a blank reason is refused`() {
        val empty = assertFailsWith<IllegalArgumentException> { verdict(reason = "") }
        assertEquals("A verdict without a reason is not a verdict", empty.message)
        val whitespace = assertFailsWith<IllegalArgumentException> { verdict(reason = "   \n\t") }
        assertEquals("A verdict without a reason is not a verdict", whitespace.message)
    }

    @Test
    fun `a verdict with a blank remedy is refused`() {
        val empty = assertFailsWith<IllegalArgumentException> {
            verdict(status = ModeStatus.BLOCKED, remedy = "")
        }
        assertEquals("A blank remedy is worse than none", empty.message)
        val whitespace = assertFailsWith<IllegalArgumentException> {
            verdict(status = ModeStatus.BLOCKED, remedy = "  ")
        }
        assertEquals("A blank remedy is worse than none", whitespace.message)
    }

    @Test
    fun `an impossible verdict may not carry a remedy`() {
        val failure = assertFailsWith<IllegalArgumentException> {
            verdict(
                status = ModeStatus.IMPOSSIBLE,
                reason = "This television has no hardware decoder for this file.",
                remedy = "Enable the setting on the TV.",
            )
        }
        assertEquals("An impossibility cannot carry a remedy", failure.message)
    }

    @Test
    fun `a not implemented verdict may not carry a remedy`() {
        val failure = assertFailsWith<IllegalArgumentException> {
            verdict(
                status = ModeStatus.NOT_IMPLEMENTED,
                reason = "The second screen is not in this build.",
                remedy = "Wait for the next update.",
            )
        }
        assertEquals(
            "Waiting for an update is not something a person can do, so it is not a remedy",
            failure.message,
        )
    }

    @Test
    fun `the statuses that may carry a remedy accept one and the others are built without`() {
        val blocked = verdict(status = ModeStatus.BLOCKED, remedy = "Turn the hotspot on.")
        assertEquals("Turn the hotspot on.", blocked.remedy)
        val available = verdict(status = ModeStatus.AVAILABLE, remedy = "Stay on this network.")
        assertEquals("Stay on this network.", available.remedy)
        assertNull(verdict(status = ModeStatus.IMPOSSIBLE).remedy)
        assertNull(verdict(status = ModeStatus.NOT_IMPLEMENTED).remedy)
        assertNull(verdict(status = ModeStatus.BLOCKED).remedy)
    }

    @Test
    fun `only an available status earns the accent tone`() {
        assertSame(ToneIntent.SIGNAL, ModePresentation.toneOf(ModeStatus.AVAILABLE))
        val accented = ModeStatus.entries.filter { ModePresentation.toneOf(it) == ToneIntent.SIGNAL }
        assertEquals(listOf(ModeStatus.AVAILABLE), accented)
    }

    @Test
    fun `blocked and impossible share the failure tone`() {
        val blocked = ModePresentation.toneOf(ModeStatus.BLOCKED)
        val impossible = ModePresentation.toneOf(ModeStatus.IMPOSSIBLE)
        assertEquals(ToneIntent.LIVE, blocked)
        assertEquals(ToneIntent.LIVE, impossible)
        // Both mean the mode is not happening, so the colour is the same on purpose. What separates
        // them on screen is the WHAT TO DO block, which only the blocked card gets.
        assertSame(blocked, impossible)
        val impossibleCard = ModePresentation.of(
            verdict(status = ModeStatus.IMPOSSIBLE, reason = "This TV can never run a receiver."),
        )
        assertNull(impossibleCard.advisoryHeading)
        val blockedCard = ModePresentation.of(
            verdict(status = ModeStatus.BLOCKED, remedy = "Turn the hotspot on."),
        )
        assertEquals(ModePresentation.WHAT_TO_DO, blockedCard.advisoryHeading)
    }

    @Test
    fun `a planned feature is not painted as a failure`() {
        val tone = ModePresentation.toneOf(ModeStatus.NOT_IMPLEMENTED)
        assertEquals(ToneIntent.NEUTRAL, tone)
        // Live is the failure tone, and a planned feature is not a failure. An unfinished mode makes
        // no claim at all, so it falls through to neutral rather than joining blocked and impossible.
        assertFalse(tone == ToneIntent.LIVE)
        assertFalse(tone == ToneIntent.SIGNAL)
    }

    @Test
    fun `no status is ever painted the hairline tone reserved for a statusless card`() {
        val tones = ModeStatus.entries.map { ModePresentation.toneOf(it) }
        assertFalse(tones.contains(ToneIntent.LINE))
        assertEquals(setOf(ToneIntent.SIGNAL, ToneIntent.LIVE, ToneIntent.NEUTRAL), tones.toSet())
        // LINE exists for a card carrying no status at all, so nothing in this file may produce it.
        assertContains(ToneIntent.entries, ToneIntent.LINE)
        assertEquals(4, ToneIntent.entries.size)
    }

    @Test
    fun `presentation copies every field of the verdict it was derived from`() {
        val source = verdict(
            mode = CastMode.SECOND_SCREEN,
            status = ModeStatus.BLOCKED,
            reason = "The TV at 192.168.49.1 has not authorised this phone yet.",
            remedy = "Accept the pairing prompt on the TV, then try again.",
        )
        val card = ModePresentation.of(source)
        assertEquals(CastMode.SECOND_SCREEN, card.mode)
        assertEquals("Second screen", card.title)
        assertEquals("Blocked", card.statusWord)
        assertEquals(ToneIntent.LIVE, card.tone)
        assertEquals("The TV at 192.168.49.1 has not authorised this phone yet.", card.reason)
        assertFalse(card.isOfferable)
        assertFalse(card.isComingSoon)
        assertTrue(card.hasRemedy)
        assertEquals("Accept the pairing prompt on the TV, then try again.", card.remedy)
        // A null remedy becomes an empty string rather than a nullable field on the card, so the UI
        // never has to decide what a null means. The blank-but-non-null arm of `hasRemedy` is
        // unreachable through ModeVerdict, whose second require() rejects a blank remedy outright.
        val withoutRemedy = ModePresentation.of(
            verdict(status = ModeStatus.IMPOSSIBLE, reason = "This TV can never run a receiver."),
        )
        assertEquals("", withoutRemedy.remedy)
        assertFalse(withoutRemedy.hasRemedy)
    }

    @Test
    fun `an available card shows neither advisory heading nor advisory body`() {
        val card = ModePresentation.of(
            verdict(
                mode = CastMode.MIRROR,
                status = ModeStatus.AVAILABLE,
                reason = "Your phone and this TV are on the same hotspot.",
            ),
        )
        assertEquals("Ready", card.statusWord)
        assertEquals(ToneIntent.SIGNAL, card.tone)
        assertTrue(card.isOfferable)
        // Nothing to advise: a working mode that grows a WHAT TO DO block is telling the person to
        // fix something that is not broken.
        assertNull(card.advisoryHeading)
        assertEquals("", card.advisoryBody)
    }

    @Test
    fun `a blocked card tells the person what to do and names the address it failed at`() {
        val card = ModePresentation.of(
            verdict(
                mode = CastMode.MIRROR,
                status = ModeStatus.BLOCKED,
                reason = "Flint could not reach the receiver at 192.168.49.1.",
                remedy = "Turn the hotspot on, then reconnect the TV to 192.168.49.1.",
            ),
        )
        val heading = assertNotNull(card.advisoryHeading)
        assertEquals("WHAT TO DO", heading)
        assertEquals(ModePresentation.WHAT_TO_DO, heading)
        assertEquals("Turn the hotspot on, then reconnect the TV to 192.168.49.1.", card.advisoryBody)
        // The sentence has to name the address it failed at, or the reader cannot tell which TV.
        assertContains(card.reason, "192.168.49.1")
        assertContains(card.advisoryBody, "192.168.49.1")
        assertTrue(card.advisoryBody.endsWith("."))
    }

    @Test
    fun `an impossible card states the fact and offers nothing to do about it`() {
        val card = ModePresentation.of(
            verdict(
                mode = CastMode.MEDIA_HANDOFF,
                status = ModeStatus.IMPOSSIBLE,
                reason = "This television cannot decode this file at its original quality.",
            ),
        )
        assertEquals("Not possible", card.statusWord)
        assertEquals(ToneIntent.LIVE, card.tone)
        assertFalse(card.hasRemedy)
        assertFalse(card.isComingSoon)
        assertFalse(card.isOfferable)
        // An impossibility with a remedy would be a lie told in a helpful tone.
        assertNull(card.advisoryHeading)
        assertEquals("", card.advisoryBody)
    }

    @Test
    fun `a coming soon card answers the question the pill provokes instead of repeating it`() {
        val card = ModePresentation.of(
            verdict(
                mode = CastMode.SECOND_SCREEN,
                status = ModeStatus.NOT_IMPLEMENTED,
                reason = "The second screen is not in this build of Flint.",
            ),
        )
        assertEquals("Coming soon", card.statusWord)
        assertEquals(ToneIntent.NEUTRAL, card.tone)
        assertTrue(card.isComingSoon)
        assertFalse(card.hasRemedy)
        assertFalse(card.isOfferable)
        assertEquals("NO ACTION NEEDED", card.advisoryHeading)
        assertEquals(ModePresentation.NO_ACTION_NEEDED, card.advisoryHeading)
        // The heading must not echo the pill above it; its job is to say whether to go and do
        // something, and the answer for an unshipped mode is no.
        assertFalse(card.advisoryHeading.equals(card.statusWord, ignoreCase = true))
        assertEquals(
            "Nothing to change on your phone or your TV — this arrives in a Flint update.",
            card.advisoryBody,
        )
        assertEquals(ModePresentation.COMING_SOON_BODY, card.advisoryBody)
        assertContains(card.advisoryBody, "Flint update")
    }

    @Test
    fun `coming soon outranks a remedy that nothing in this file forbids`() {
        // ModeVerdict is what keeps this pair apart: a NOT_IMPLEMENTED verdict with a remedy throws.
        assertFailsWith<IllegalArgumentException> {
            verdict(status = ModeStatus.NOT_IMPLEMENTED, remedy = "Turn the hotspot on.")
        }
        // ModePresentation itself makes isComingSoon and hasRemedy two independent booleans and does
        // nothing to make them mutually exclusive, so the pair is constructible here. Pinning the
        // resolution means the day an assessor does produce it, the card stays honest instead of
        // telling someone to go and fix an unshipped feature.
        val card = ModePresentation(
            mode = CastMode.SECOND_SCREEN,
            title = ModePresentation.titleOf(CastMode.SECOND_SCREEN),
            statusWord = StatusWord.NOT_IMPLEMENTED,
            tone = ToneIntent.NEUTRAL,
            reason = "The second screen is not in this build of Flint.",
            isOfferable = false,
            isComingSoon = true,
            hasRemedy = true,
            remedy = "Turn the hotspot on.",
        )
        assertEquals(ModePresentation.NO_ACTION_NEEDED, card.advisoryHeading)
        assertEquals(ModePresentation.COMING_SOON_BODY, card.advisoryBody)
        assertFalse(card.advisoryBody.contains("hotspot"))
    }

    @Test
    fun `verdicts and presentations compare copy and print by value`() {
        val blocked = verdict(status = ModeStatus.BLOCKED, remedy = "Turn the hotspot on.")
        assertEquals(blocked, blocked.copy())
        assertEquals(blocked.hashCode(), blocked.copy().hashCode())
        assertFalse(blocked == blocked.copy(mode = CastMode.MEDIA_HANDOFF))
        assertFalse(blocked == blocked.copy(status = ModeStatus.AVAILABLE))
        assertFalse(blocked == blocked.copy(reason = "Something else entirely."))
        assertFalse(blocked == blocked.copy(remedy = null))
        assertEquals(CastMode.MIRROR, blocked.component1())
        assertEquals(ModeStatus.BLOCKED, blocked.component2())
        assertEquals(blocked.reason, blocked.component3())
        assertEquals("Turn the hotspot on.", blocked.component4())
        assertContains(blocked.toString(), "MIRROR")

        val card = ModePresentation.of(blocked)
        assertEquals(card, ModePresentation.of(blocked))
        assertEquals(card.hashCode(), ModePresentation.of(blocked).hashCode())
        assertFalse(card.equals(blocked))
        assertFalse(card == card.copy(title = "Something else"))
        assertFalse(card == card.copy(statusWord = "Ready"))
        assertFalse(card == card.copy(tone = ToneIntent.LINE))
        assertFalse(card == card.copy(isOfferable = true))
        assertFalse(card == card.copy(isComingSoon = true))
        assertFalse(card == card.copy(hasRemedy = false))
        assertFalse(card == card.copy(remedy = ""))
        assertFalse(card == card.copy(reason = "Something else entirely."))
        assertFalse(card == card.copy(mode = CastMode.MEDIA_HANDOFF))
        assertEquals(CastMode.MIRROR, card.component1())
        assertEquals("Mirror this screen", card.component2())
        assertEquals("Blocked", card.component3())
        assertEquals(ToneIntent.LIVE, card.component4())
        assertEquals(blocked.reason, card.component5())
        assertFalse(card.component6())
        assertFalse(card.component7())
        assertTrue(card.component8())
        assertEquals("Turn the hotspot on.", card.component9())
        assertContains(card.toString(), "Mirror this screen")
    }

    private fun verdict(
        mode: CastMode = CastMode.MIRROR,
        status: ModeStatus = ModeStatus.AVAILABLE,
        reason: String = "Your phone and this TV are on the same hotspot.",
        remedy: String? = null,
    ) = ModeVerdict(mode = mode, status = status, reason = reason, remedy = remedy)
}
