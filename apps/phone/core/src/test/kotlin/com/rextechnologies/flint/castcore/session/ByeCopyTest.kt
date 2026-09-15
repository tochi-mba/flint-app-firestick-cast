package com.rextechnologies.flint.castcore.session

import com.rextechnologies.flint.protocol.wire.ByeMessage
import com.rextechnologies.flint.protocol.wire.ByeReason
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

class SessionFailureTest {
    @Test
    fun `a failure without a sentence is not a failure`() {
        assertFailsWith<IllegalArgumentException> {
            SessionFailure(SessionFailureKind.PROTOCOL, " ")
        }
        assertFailsWith<IllegalArgumentException> {
            SessionFailure(SessionFailureKind.PROTOCOL, "Something.", remedy = " ")
        }
    }
}

class ByeCopyTest {
    @Test
    fun `the busy refusal is recognised by its detail, not by its reason`() {
        // The receiver admits one phone at a time and refuses the second with PROTOCOL_ERROR, after
        // it has already answered the HELLO and validated the credential. A phone that read the
        // reason alone would report a protocol failure for the most ordinary situation there is.
        val bye = ByeMessage(ByeReason.PROTOCOL_ERROR, ByeCopy.RECEIVER_BUSY_DETAIL)
        val failure = ByeCopy.of(bye)
        assertEquals(SessionFailureKind.RECEIVER_BUSY, failure.kind)
        assertTrue(failure.sentence.startsWith("This TV is already casting from another phone"))
        assertNotNull(failure.remedy)
    }

    @Test
    fun `an authentication refusal asks for the code again and explains why the old one failed`() {
        val failure = ByeCopy.of(ByeMessage(ByeReason.AUTHENTICATION_FAILED, "Authentication failed"))
        assertEquals(SessionFailureKind.AUTHENTICATION, failure.kind)
        val remedy = assertNotNull(failure.remedy)
        assertTrue(remedy.contains("only good for one pairing"), remedy)
    }

    @Test
    fun `a version mismatch points at the older end`() {
        val failure = ByeCopy.of(ByeMessage(ByeReason.UNSUPPORTED_VERSION, "No common protocol version"))
        assertEquals(SessionFailureKind.VERSION, failure.kind)
        val remedy = assertNotNull(failure.remedy)
        assertTrue(remedy.contains("older"), remedy)
    }

    @Test
    fun `a deliberate stop by the television is not dressed up as a fault`() {
        assertEquals(
            SessionFailureKind.RECEIVER_STOPPED,
            ByeCopy.of(ByeMessage(ByeReason.RECEIVER_STOPPED)).kind,
        )
        val normal = ByeCopy.of(ByeMessage(ByeReason.NORMAL))
        assertEquals(SessionFailureKind.RECEIVER_STOPPED, normal.kind)
        assertNull(normal.remedy)
    }

    @Test
    fun `a protocol refusal quotes the television rather than paraphrasing it`() {
        val failure = ByeCopy.of(ByeMessage(ByeReason.PROTOCOL_ERROR, "Malformed frame"))
        assertEquals(SessionFailureKind.PROTOCOL, failure.kind)
        assertEquals("Malformed frame.", failure.sentence)
        assertNull(failure.remedy)
    }

    @Test
    fun `a refusal with nothing to say says that rather than inventing a cause`() {
        val failure = ByeCopy.of(ByeMessage(ByeReason.PROTOCOL_ERROR, ""))
        assertTrue(failure.sentence.contains("without saying why"), failure.sentence)
    }

    @Test
    fun `a detail that already ends in punctuation does not gain a second full stop`() {
        assertEquals(
            "It went wrong.",
            ByeCopy.of(ByeMessage(ByeReason.PROTOCOL_ERROR, "It went wrong.")).sentence,
        )
        assertEquals(
            "Why?",
            ByeCopy.of(ByeMessage(ByeReason.PROTOCOL_ERROR, "Why?")).sentence,
        )
    }

    @Test
    fun `the socket dying is never reported as though the television said something`() {
        val silent = ByeCopy.transportFailure("")
        assertEquals(SessionFailureKind.TRANSPORT, silent.kind)
        assertEquals("The connection to the television dropped.", silent.sentence)

        val detailed = ByeCopy.transportFailure("Connection reset")
        assertTrue(detailed.sentence.endsWith("Connection reset."), detailed.sentence)
        assertNotNull(detailed.remedy)
    }
}
