package com.rextechnologies.flint.receiver.browser

import com.rextechnologies.flint.protocol.wire.BrowserInputMessage
import com.rextechnologies.flint.protocol.wire.BrowserSemanticKey
import com.rextechnologies.flint.protocol.wire.BrowserSemanticKeyInput
import com.rextechnologies.flint.protocol.wire.BrowserTextInput
import kotlin.test.Test
import kotlin.test.assertIs
import kotlin.test.assertTrue

class BrowserInputRouterTest {
    @Test
    fun `semantic input is accepted only after the browser epoch is enabled`() {
        val accepted = mutableListOf<BrowserNativeInput>()
        val router = BrowserInputRouter(onAccepted = { accepted += it })

        val rejected = router.handle(
            BrowserInputMessage(1, 1, BrowserSemanticKeyInput(BrowserSemanticKey.SELECT)),
        )
        assertIs<BrowserInputMapping.Rejected>(rejected)

        router.enableForBrowserEpoch(1)
        val mapped = router.handle(
            BrowserInputMessage(1, 1, BrowserSemanticKeyInput(BrowserSemanticKey.SELECT)),
        )
        assertIs<BrowserInputMapping.Accepted>(mapped)
        assertTrue(accepted.single() is BrowserNativeInput.KeyStroke)
    }

    @Test
    fun `text input is redacted in diagnostics and accepted when valid`() {
        val accepted = mutableListOf<BrowserNativeInput>()
        val router = BrowserInputRouter(onAccepted = { accepted += it })
        router.enableForBrowserEpoch(2)

        val mapped = router.handle(
            BrowserInputMessage(2, 1, BrowserTextInput("hello")),
        )

        assertIs<BrowserInputMapping.Accepted>(mapped)
        assertIs<BrowserNativeInput.ComposedText>(accepted.single())
        assertTrue(!accepted.single().toString().contains("hello"))
    }
}
