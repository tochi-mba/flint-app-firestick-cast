package com.rextechnologies.flint.receiver.vpn

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class WireGuardConfigValidatorTest {
    @Test
    fun `requires both Interface and Peer section headers`() {
        assertTrue(
            WireGuardConfigValidator.isValid(
                """
                [Interface]
                PrivateKey = aaa
                Address = 10.0.0.2/32

                [Peer]
                PublicKey = bbb
                Endpoint = 203.0.113.1:51820
                """.trimIndent(),
            ),
        )
        assertFalse(WireGuardConfigValidator.isValid(""))
        assertFalse(WireGuardConfigValidator.isValid("[Interface]\nPrivateKey = aaa\n"))
        assertFalse(WireGuardConfigValidator.isValid("[Peer]\nPublicKey = bbb\n"))
        assertFalse(WireGuardConfigValidator.isValid("Interface]\n[Peer]\n"))
    }

    @Test
    fun `section headers are matched case-sensitively on their own line`() {
        assertFalse(
            WireGuardConfigValidator.isValid(
                """
                [interface]
                PrivateKey = aaa
                [peer]
                PublicKey = bbb
                """.trimIndent(),
            ),
        )
        assertTrue(
            WireGuardConfigValidator.isValid(
                "  [Interface]  \nPrivateKey = aaa\n\n\t[Peer]\nPublicKey = bbb\n",
            ),
        )
    }
}
