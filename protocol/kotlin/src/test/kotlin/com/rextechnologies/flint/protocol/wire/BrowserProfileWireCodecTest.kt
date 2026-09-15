package com.rextechnologies.flint.protocol.wire

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFailsWith
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class BrowserProfileWireCodecTest {
    @Test
    fun allProfileActionsAndSourcesRoundTrip() {
        val commands = listOf(
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.SELECT_TV_PROFILE, "family"),
            BrowserProfileCommandMessage(3, 2, BrowserProfileAction.CREATE_TV_PROFILE, name = "Kids 🚀"),
            BrowserProfileCommandMessage(3, 3, BrowserProfileAction.RENAME_TV_PROFILE, "family_2", "Family room"),
            BrowserProfileCommandMessage(3, 4, BrowserProfileAction.DELETE_TV_PROFILE, "family-2"),
            BrowserProfileCommandMessage(3, 5, BrowserProfileAction.SELECT_DEVICE),
            BrowserProfileCommandMessage(3, 6, BrowserProfileAction.REQUEST_SNAPSHOT),
        )
        val states = listOf(
            BrowserProfileStateMessage(
                3,
                7,
                BrowserProfileSource.TV,
                "family",
                "",
                listOf(BrowserProfileEntry("family", "Family"), BrowserProfileEntry("kids_2", "Kids 🚀")),
            ),
            BrowserProfileStateMessage(
                3,
                8,
                BrowserProfileSource.DEVICE,
                "",
                "Tochukwu's PC",
                listOf(BrowserProfileEntry("family", "Family")),
            ),
            BrowserProfileStateMessage(3, 9, BrowserProfileSource.DEVICE, "", "", emptyList()),
        )

        (commands + states).forEach { message ->
            val frame = WireFrame(2, message)
            assertEquals(frame, WireCodec.decode(WireCodec.encode(frame)))
        }
    }

    @Test
    fun profileMessagesAreAdditiveTlsOnlyV2BrowserTypes() {
        assertEquals(28, WireMessageType.BROWSER_PROFILE_COMMAND.id)
        assertEquals(29, WireMessageType.BROWSER_PROFILE_STATE.id)
        val messages = listOf<WireMessage>(
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.REQUEST_SNAPSHOT),
            BrowserProfileStateMessage(3, 1, BrowserProfileSource.DEVICE, "", "PC", emptyList()),
        )
        messages.forEach { message ->
            assertTrue(BrowserWireRules.isBrowserMessage(message))
            assertTrue(BrowserWireRules.isBrowserType(message.typeId))
            assertTrue(BrowserWireRules.isForbiddenOnOrdinaryChannel(message))
            assertFailsWith<WireFormatException> { WireCodec.encode(WireFrame(1, message)) }
        }
        assertTrue(BrowserWireRules.isBrowserType(30))
        assertTrue(BrowserWireRules.isBrowserType(31))
        assertTrue(BrowserWireRules.isBrowserType(32))
        assertTrue(BrowserWireRules.isBrowserType(33))
        assertTrue(BrowserWireRules.isBrowserType(34))
        assertFalse(BrowserWireRules.isBrowserType(37))
    }

    @Test
    fun invalidActionFieldsIdentifiersAndNamesAreRejected() {
        val valid = BrowserProfileCommandMessage(3, 1, BrowserProfileAction.SELECT_TV_PROFILE, "family")
        val invalid = listOf(
            valid.copy(epoch = 0),
            valid.copy(commandId = 0),
            valid.copy(profileId = ""),
            valid.copy(profileId = "bad id"),
            valid.copy(profileId = "a".repeat(BrowserWireLimits.MAX_PROFILE_ID_BYTES + 1)),
            valid.copy(name = "extra"),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CREATE_TV_PROFILE, "generated", "Family"),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CREATE_TV_PROFILE, name = ""),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CREATE_TV_PROFILE, name = " Family"),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CREATE_TV_PROFILE, name = "Family\nRoom"),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.CREATE_TV_PROFILE, name = "🚀".repeat(17)),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.RENAME_TV_PROFILE, "family", ""),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.DELETE_TV_PROFILE, "family", "extra"),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.SELECT_DEVICE, "family"),
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.REQUEST_SNAPSHOT, name = "extra"),
        )

        invalid.forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) { WireCodec.encode(WireFrame(2, message)) }
        }
    }

    @Test
    fun invalidStateOwnershipCatalogAndDisplayValuesAreRejected() {
        val profiles = listOf(BrowserProfileEntry("family", "Family"))
        val tv = BrowserProfileStateMessage(3, 1, BrowserProfileSource.TV, "family", "", profiles)
        val device = BrowserProfileStateMessage(3, 1, BrowserProfileSource.DEVICE, "", "PC", profiles)
        val invalid = listOf(
            tv.copy(epoch = 0),
            tv.copy(revision = 0),
            tv.copy(activeProfileId = ""),
            tv.copy(activeProfileId = "missing"),
            device.copy(activeProfileId = "family"),
            device.copy(deviceName = "p".repeat(BrowserWireLimits.MAX_DEVICE_PROFILE_NAME_BYTES + 1)),
            device.copy(deviceName = "PC\u0000"),
            tv.copy(profiles = List(BrowserWireLimits.MAX_TV_PROFILES + 1) {
                BrowserProfileEntry("p$it", "Profile $it")
            }),
            tv.copy(profiles = listOf(BrowserProfileEntry("bad id", "Family"))),
            tv.copy(profiles = listOf(BrowserProfileEntry("family", " Family"))),
            tv.copy(profiles = listOf(BrowserProfileEntry("family", "Family\nRoom"))),
            tv.copy(profiles = listOf(
                BrowserProfileEntry("family", "Family"),
                BrowserProfileEntry("family", "Other"),
            )),
            tv.copy(profiles = listOf(
                BrowserProfileEntry("family", "Family"),
                BrowserProfileEntry("other", "Family"),
            )),
        )

        invalid.forEach { message ->
            assertFailsWith<WireFormatException>(message.toString()) { WireCodec.encode(WireFrame(2, message)) }
        }
    }

    @Test
    fun profileDecodersRejectUnknownEnumsAndTrailingBytes() {
        fun payload(message: WireMessage): ByteArray {
            val encoded = WireCodec.encode(WireFrame(2, message))
            return encoded.copyOfRange(12, encoded.size)
        }

        val command = payload(
            BrowserProfileCommandMessage(3, 1, BrowserProfileAction.REQUEST_SNAPSHOT),
        ).also { it[16] = 0 }
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(2, WireMessageType.BROWSER_PROFILE_COMMAND.id, command)
        }
        val state = payload(
            BrowserProfileStateMessage(3, 1, BrowserProfileSource.DEVICE, "", "PC", emptyList()),
        ).also { it[16] = 0 }
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(2, WireMessageType.BROWSER_PROFILE_STATE.id, state)
        }
        val valid = payload(
            BrowserProfileStateMessage(3, 1, BrowserProfileSource.DEVICE, "", "PC", emptyList()),
        )
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(2, WireMessageType.BROWSER_PROFILE_STATE.id, valid + 0)
        }

        val excessiveCount = payload(
            BrowserProfileStateMessage(3, 1, BrowserProfileSource.DEVICE, "", "", emptyList()),
        ).also { it[21] = (BrowserWireLimits.MAX_TV_PROFILES + 1).toByte() }
        assertFailsWith<WireFormatException> {
            WireMessageCodec.decode(2, WireMessageType.BROWSER_PROFILE_STATE.id, excessiveCount)
        }
    }
}
